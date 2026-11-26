using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("user")]
    [InlineData("system")]
    [InlineData("call-id")]
    [InlineData("call-name")]
    [InlineData("arguments")]
    [InlineData("result-id")]
    [InlineData("result-replacement")]
    [InlineData("result-exception")]
    public async Task PackageCompanionCannotReplaceCanonicalInputOrAcquireDisclosureAuthority(string fault)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = ToolContext(root.Request, f.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var source = ModelToolProtocol.ReadContext(context.Serialize(), provenance);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : HostedClaudeSessionRequestTests.Proposal(HostedClaudeSessionRequestTests.VersionProposal));
        var reviews = 0;
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        await using var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => { reviews++; return ValueTask.FromResult(true); }, _ => ValueTask.FromResult(true), Token))!;
        var first = await transport.CompleteAsync(ChatRequest(provenance, profile, source, [], 1), Token);
        first.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var calls = (ModelAssistantToolCalls)first.Message!;
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0],
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "Not observed")));
        var companion = ChatRequest(provenance, profile, source, [calls, result], 2).ChatMessages.ToList();
        var call = (FunctionCallContent)companion[1].Contents[0];
        var functionResult = (FunctionResultContent)companion[2].Contents[0];
        switch (fault)
        {
            case "missing": companion.Clear(); break;
            case "user": companion[0] = new(ChatRole.User, "replacement"); break;
            case "system": companion.Insert(0, new(ChatRole.System, "replacement policy")); break;
            case "call-id": companion[1].Contents[0] = new FunctionCallContent("foreign", call.Name, call.Arguments); break;
            case "call-name": companion[1].Contents[0] = new FunctionCallContent(call.CallId, "foreign", call.Arguments); break;
            case "arguments": companion[1].Contents[0] = new FunctionCallContent(call.CallId, call.Name,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["extra"] = true }); break;
            case "result-id": companion[2].Contents[0] = new FunctionResultContent("foreign", result); break;
            case "result-replacement": companion[2].Contents[0] = new FunctionResultContent(functionResult.CallId,
                ModelToolChatMessages.Text(result)); break;
            case "result-exception": functionResult.Exception = new InvalidOperationException("synthetic"); break;
        }
        var request = ModelToolProtocol.CreateRequest(provenance, profile, source, [calls, result], 2,
            chatMessages: companion);
        var reply = await transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        reply.Message.Should().BeNull(); reply.Usage.Should().BeNull();
        reviews.Should().Be(2);
        f.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Official_sdk_tool_exchange_never_retries_or_reuses_review_after_http_failure(int status)
    {
        var fixture = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = fixture.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = fixture.Profile(catalogue);
        var context = ToolContext(root.Request, fixture.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var reviews = 0;
        fixture.Respond = message => message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":25}""")
            : new HttpResponseMessage((System.Net.HttpStatusCode)status)
            {
                Content = new StringContent("""{"error":{"type":"overloaded_error","message":"synthetic failure"}}"""),
                Headers = { { "Retry-After", "0" } },
            };
        await using var runtime = new ClaudeMessagesRuntime(broker, fixture.Time,
            NullLogger<ClaudeMessagesRuntime>.Instance, fixture.CreateHttp);
        await using var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => { reviews++; return ValueTask.FromResult(true); }, _ => ValueTask.FromResult(true), Token))!;
        var reply = await transport.CompleteAsync(ChatRequest(provenance, profile,
            ModelToolProtocol.ReadContext(context.Serialize(), provenance), [], 1), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        reply.TokenUsage.Should().BeNull();
        reviews.Should().Be(2);
        fixture.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(-1, ModelTurnOutcome.DeniedEgress)]
    public async Task BothPhysicalInputBodiesAreChargedAtTheExactCumulativeBoundaryBeforeAnySend(int adjustment, ModelTurnOutcome expected)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = ToolContext(root.Request, f.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var source = ModelToolProtocol.ReadContext(context.Serialize(), provenance);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var initial = ChatRequest(provenance, profile, source, [], 1);
        var baselineTransport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token))!;
        await using (baselineTransport)
        { (await baselineTransport.CompleteAsync(initial, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
        var bytes = Math.Max(ModelToolProtocol.SerializeRequest(initial).Length,
            f.Requests.Where(item => item.Method is "POST").Sum(item => item.Body.Length));
        f.Requests.Clear();
        var request = ChatRequest(provenance, profile, source, [], 1,
            new(bytes + adjustment, ModelToolProtocol.MaximumOutputUtf8Bytes));
        var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token))!;
        runtime.IsQuiescent.Should().BeFalse();
        var result = await transport.CompleteAsync(request, Token);
        result.Outcome.Should().Be(expected);
        (result.Usage?.InputUtf8Bytes).Should().Be(expected == ModelTurnOutcome.Succeeded ? bytes : null);
        result.TokenUsage.Should().Be(expected == ModelTurnOutcome.Succeeded ? new ModelTokenUsage(25, 5, 25) : null);
        f.Requests.Count(item => item.Method is "POST").Should().Be(expected == ModelTurnOutcome.Succeeded ? 2 : 0);
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(-1, ModelTurnOutcome.Unknown)]
    public async Task CountAndCompleteRawResponseShareExactRemainingOutputBudget(int adjustment, ModelTurnOutcome expected)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = ToolContext(root.Request, f.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        const string counted = """{"input_tokens":25}""";
        var bytes = Encoding.UTF8.GetByteCount(counted + Answer);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? counted : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        await using var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token))!;
        var request = ChatRequest(provenance, profile, ModelToolProtocol.ReadContext(context.Serialize(), provenance),
            [], 1, new(ModelToolProtocol.MaximumInputUtf8Bytes, bytes + adjustment));
        var result = await transport.CompleteAsync(request, Token);
        result.Outcome.Should().Be(expected);
        (result.Usage?.OutputUtf8Bytes).Should().Be(expected == ModelTurnOutcome.Succeeded ? bytes : null);
        f.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("account-await-host")]
    [InlineData("account")]
    [InlineData("replacement")]
    [InlineData("deployment")]
    [InlineData("catalogue")]
    [InlineData("cancel")]
    public async Task ChangedAuthorityBeforeContinuationConsentCannotSendPreviouslyExecutedResult(string fault)
    {
        var clock = new ObservationClock();
        var f = new ClaudeAccountBrokerTests.Fixture { Time = clock };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = ToolContext(root.Request, f.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : HostedClaudeSessionRequestTests.Proposal(HostedClaudeSessionRequestTests.VersionProposal));
        var live = true;
        var offers = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        await using var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context, async (_, _) =>
        {
            if (++offers == 3)
            {
                if (fault is "host") { live = false; }
                if (fault is "account-await-host") { f.BeforeApprovalRead = () => live = false; }
                if (fault is "account") { broker.Revoke(); }
                if (fault is "replacement")
                {
                    f.Respond = null;
                    await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
                }
                if (fault is "deployment") { f.Approved = false; }
                if (fault is "catalogue") { clock.Now += TimeSpan.FromMinutes(10); }
                if (fault is "cancel") { await cancellation.CancelAsync(); }
            }
            return true;
        }, _ => ValueTask.FromResult(live), cancellation.Token))!;
        var source = ModelToolProtocol.ReadContext(context.Serialize(), provenance);
        var first = await transport.CompleteAsync(ChatRequest(provenance, profile, source, [], 1), Token);
        first.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var calls = (ModelAssistantToolCalls)first.Message!;
        var observed = ModelToolProtocol.CreateToolResult(calls.Calls[0], new(CapabilityOutcome.Succeeded, "observed",
            Version: new("1.0", "Not observed by the current version provider.")));
        var second = await transport.CompleteAsync(ChatRequest(provenance, profile, source, [calls, observed], 2,
            new(ModelToolProtocol.MaximumInputUtf8Bytes - first.Usage!.InputUtf8Bytes,
                ModelToolProtocol.MaximumOutputUtf8Bytes - first.Usage.OutputUtf8Bytes - ModelToolProtocol.SerializeMessage(observed).Length)), Token);
        second.Outcome.Should().BeOneOf(ModelTurnOutcome.DeniedEgress, ModelTurnOutcome.Cancelled);
        second.Message.Should().BeNull();
        f.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelIgnoringObservationHttpStaysOwnedUntilPhysicalCompletionAndCannotPublishLateAnswer(bool accountRevocation)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = ToolContext(root.Request, f.Time);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return Task.FromResult(ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":25}""")); }
            entered.TrySetResult(); return release.Task;
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), cancellation.Token))!;
        var run = transport.CompleteAsync(ChatRequest(provenance, profile,
            ModelToolProtocol.ReadContext(context.Serialize(), provenance), [], 1), Token);
        await entered.Task.WaitAsync(Token);
        if (accountRevocation) { broker.Revoke(); } else { await cancellation.CancelAsync(); }
        var closingTransport = transport.DisposeAsync().AsTask();
        var repeatedClose = transport.DisposeAsync().AsTask();
        var closingRuntime = runtime.DisposeAsync().AsTask();
        runtime.IsQuiescent.Should().BeFalse();
        run.IsCompleted.Should().BeFalse(); closingTransport.IsCompleted.Should().BeFalse();
        repeatedClose.IsCompleted.Should().BeFalse(); closingRuntime.IsCompleted.Should().BeFalse();
        release.SetResult(ClaudeAccountBrokerTests.Fixture.Json(Answer));
        var result = await run;
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled); result.Message.Should().BeNull();
        await closingTransport; await repeatedClose; await closingRuntime;
        runtime.IsQuiescent.Should().BeTrue();
        f.Requests.Count(item => item.Method is "POST").Should().Be(2);
    }

    private static ModelContextEnvelope ToolContext(HostRequest request, TimeProvider time) =>
        new(request, "policy", "original request", [], time.GetUtcNow(), time.GetUtcNow().AddMinutes(2),
            ModelToolMode.ReadOnly, ClaudeObservationTools.Constrain([ReadOnlyCapabilityCatalog.Version]));

    private static ModelToolRequest ChatRequest(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelToolContext context, IEnumerable<ModelToolMessage> history, int round, ModelToolBudget? budget = null)
    {
        var canonical = history.ToArray();
        List<ChatMessage> messages = [new(ChatRole.User, context.UserRequest)];
        foreach (var item in canonical)
        {
            messages.Add(item switch
            {
                ModelAssistantToolCalls calls => new(ChatRole.Assistant, calls.Calls.Select(call =>
                    (AIContent)new FunctionCallContent(call.CallId, call.CapabilityId,
                        ModelToolChatMessages.Arguments(call.InputJson))).ToList()),
                ModelToolResultMessage result => new(ChatRole.Tool, [new FunctionResultContent(result.CallId, result)]),
                _ => throw new InvalidDataException("Unsupported observation test history."),
            });
        }
        return ModelToolProtocol.CreateRequest(provenance, profile, context, canonical, round, budget,
            chatMessages: messages);
    }

    private sealed class ObservationClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
