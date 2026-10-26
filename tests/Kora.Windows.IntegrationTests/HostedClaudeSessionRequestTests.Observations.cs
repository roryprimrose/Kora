using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    internal const string VersionProposal = """{"type":"tool_use","id":"call_version_1","name":"kora_application_get_version","input":{}}""";
    internal static string Proposal(string content) =>
        """{"id":"msg_test","type":"message","role":"assistant","model":"exact-model","content":["""
        + content + """],"stop_reason":"tool_use","stop_sequence":null,"usage":{"input_tokens":25,"output_tokens":5}}""";

    [Theory]
    [InlineData(RequestOrigin.LocalUi, 4)]
    [InlineData(RequestOrigin.ActivatedVoice, 4)]
    [InlineData(RequestOrigin.LocalUi, 0)]
    [InlineData(RequestOrigin.LocalUi, 1)]
    [InlineData(RequestOrigin.ActivatedVoice, 2)]
    [InlineData(RequestOrigin.ActivatedVoice, 3)]
    public async Task RealWireProposalDispatchAndResultContinuationRequireFourIndependentNativeConsents(RequestOrigin origin, int approvals)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        const string original = "  exact original\r\nobservation request  ";
        var commands = new SessionRequestCommandService(c.Workspace, c.Conversation, c.Access, f.Time);
        var review = (await commands.ExecuteAsync(SessionCommand.Parse(
            "session request review active claude-tools=application.get_version -- " + original, "Kora")!,
            origin, () => true, f.Token)).RequestReview!;
        review.ClaudeTools.Should().Equal(ReadOnlyCapabilityCatalog.Version);
        c.Boundary.VersionReads.Should().Be(0);
        var messages = 0;
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : ++messages == 1 ? Proposal(VersionProposal) : ClaudeMessagesRuntimeTests.Answer);
        var offered = 0;
        HostedTransmissionReview? previous = null;
        c.Reviews.ClaudeOffered += disclosure =>
        {
            c.Http.Requests.Count(item => item.Method is "POST").Should().Be(offered);
            c.Boundary.VersionReads.Should().Be(offered >= 2 ? 1 : 0);
            using var json = JsonDocument.Parse(disclosure.Body.ToArray());
            json.RootElement.GetProperty("tools").GetArrayLength().Should().Be(1);
            json.RootElement.GetProperty("tools")[0].GetProperty("name").GetString().Should().Be("kora_application_get_version");
            using var presentation = disclosure.Presentation();
            if (previous is not null) { c.Reviews.Confirm(previous, true, RequestOrigin.LocalUi).Should().BeFalse(); }
            c.Reviews.Confirm(disclosure, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
            c.Reviews.Confirm(disclosure, ++offered <= approvals, RequestOrigin.LocalUi).Should().BeTrue();
            previous = disclosure;
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var receipt = (await commands.ExecuteAsync(SessionCommand.Parse("session request confirm", "Kora")!,
            RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Body.Should().Be(original);
        receipt.Message.Outcome.Should().Be(approvals == 4 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        (receipt.Message.AssistantResponse?.Body).Should().Be(approvals == 4 ? "genuine exact answer" : null);
        c.Boundary.VersionReads.Should().Be(approvals >= 2 ? 1 : 0);
        var posts = c.Http.Requests.Where(item => item.Method is "POST").ToArray();
        posts.Should().HaveCount(approvals);
        if (approvals >= 3)
        {
            using var continuation = JsonDocument.Parse(posts[2].Body);
            var history = continuation.RootElement.GetProperty("messages");
            history[0].GetProperty("role").GetString().Should().Be("user");
            history[0].GetProperty("content").GetString().Should().Be(original);
            history[1].GetProperty("role").GetString().Should().Be("assistant");
            history[1].GetProperty("content")[0].GetProperty("id").GetString().Should().Be("call_version_1");
            history[2].GetProperty("role").GetString().Should().Be("user");
            var result = history[2].GetProperty("content")[0];
            result.GetProperty("type").GetString().Should().Be("tool_result");
            result.GetProperty("tool_use_id").GetString().Should().Be("call_version_1");
            using var observed = JsonDocument.Parse(result.GetProperty("content").GetString()!);
            observed.RootElement.GetProperty("version").GetProperty("version").GetString().Should().Be("1.0.0");
        }
        (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, receipt.Message.Request.RequestId, f.Token))
            .Should().Be(receipt.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ExecuteAsync(SessionCommand.Parse("session request confirm", "Kora")!,
            RequestOrigin.LocalUi, () => true, f.Token));
        c.Turns.IsQuiescent.Should().BeTrue(); c.Runtime.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(original).And.NotContain("genuine exact answer");
    }

    [Theory]
    [InlineData("unknown-name")]
    [InlineData("unknown-input")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-field")]
    [InlineData("extra-field")]
    [InlineData("malformed")]
    [InlineData("oversize")]
    [InlineData("mixed-prose")]
    [InlineData("server-tool")]
    public async Task EntireNativeProposalIsValidatedBeforeAnyHostEffectAndNeverRetainedAsAnswer(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var proposal = fault switch
        {
            "unknown-name" => Proposal(VersionProposal.Replace("kora_application_get_version", "provider_native_tool", StringComparison.Ordinal)),
            "unknown-input" => Proposal(VersionProposal.Replace("\"input\":{}", "\"input\":{\"path\":\"not permitted\"}", StringComparison.Ordinal)),
            "duplicate-id" => Proposal(VersionProposal + "," + VersionProposal),
            "duplicate-field" => Proposal(VersionProposal.Replace("\"input\":{}", "\"input\":{},\"input\":{}", StringComparison.Ordinal)),
            "extra-field" => Proposal(VersionProposal.Replace("\"input\":{}", "\"input\":{},\"approved\":true", StringComparison.Ordinal)),
            "server-tool" => Proposal(VersionProposal.Replace("\"tool_use\"", "\"server_tool_use\"", StringComparison.Ordinal)),
            "mixed-prose" => Proposal("""{"type":"text","text":"not a final answer"},""" + VersionProposal),
            "oversize" => new string('x', 4097),
            _ => "{",
        };
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : proposal);
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var receipt = await c.Conversation.SubmitClaudeObservationsAsync(target, "original",
            [ReadOnlyCapabilityCatalog.Version], () => true, f.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Boundary.VersionReads.Should().Be(0);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
        c.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhysicalNetworkFailureIsTerminalWithoutRetryOrFallback(bool afterTool)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var messages = 0;
        c.Http.Respond = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":25}"""); }
            if (++messages == 1 && afterTool) { return ClaudeAccountBrokerTests.Fixture.Json(Proposal(VersionProposal)); }
            throw new HttpRequestException("Synthetic partial network failure.");
        };
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var receipt = await c.Conversation.SubmitClaudeObservationsAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "original", [ReadOnlyCapabilityCatalog.Version], () => true, f.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Boundary.VersionReads.Should().Be(afterTool ? 1 : 0);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(afterTool ? 4 : 2);
        c.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateCallIdAcrossRoundsStopsBeforeASecondHostExecution()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : Proposal(VersionProposal));
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var receipt = await c.Conversation.SubmitClaudeObservationsAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "original", [ReadOnlyCapabilityCatalog.Version], () => true, f.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Message.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Boundary.VersionReads.Should().Be(1);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(4);
    }

    [Theory]
    [InlineData(ReadOnlyCapabilityCatalog.List, """{"count":1}""", "capabilities")]
    [InlineData(ReadOnlyCapabilityCatalog.Get, """{"id":"application.get_version"}""", "descriptor")]
    [InlineData(ReadOnlyCapabilityCatalog.Version, "{}", "version")]
    [InlineData(ReadOnlyCapabilityCatalog.Readiness, "{}", "readiness")]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeList, "{}", "runtimes")]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeStatus, """{"id":"local.inference"}""", "runtime")]
    public async Task EachOriginalObservationUsesItsExactAdvertisedSchemaAndActualHostResult(string id, string input, string resultProperty)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var messages = 0;
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : ++messages == 1
                ? Proposal("{\"type\":\"tool_use\",\"id\":\"exact_call_1\",\"name\":\""
                    + Kora.Windows.Dependencies.ClaudeObservationWire.Name(id) + "\",\"input\":" + input + "}")
                : ClaudeMessagesRuntimeTests.Answer);
        c.Reviews.ClaudeOffered += review =>
        {
            using var json = JsonDocument.Parse(review.Body.ToArray());
            var schema = json.RootElement.GetProperty("tools")[0].GetProperty("input_schema");
            schema.GetRawText().Should().Be(ReadOnlyCapabilitySchema.Serialize(id));
            var history = json.RootElement.GetProperty("messages");
            if (history.GetArrayLength() > 1)
            {
                using var result = JsonDocument.Parse(history[2].GetProperty("content")[0].GetProperty("content").GetString()!);
                result.RootElement.TryGetProperty(resultProperty, out _).Should().BeTrue();
            }
            using var presentation = review.Presentation();
            c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var receipt = await c.Conversation.SubmitClaudeObservationsAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "original", [id], () => true, f.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("genuine exact answer");
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(4);
    }

    [Fact]
    public async Task NativeToolLoopCannotExceedSixActualCallsOrRetryAfterExhaustion()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var messages = 0;
        c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? """{"input_tokens":25}""" : ++messages == 1
                ? Proposal(string.Join(",", Enumerable.Range(1, 6).Select(index => VersionProposal.Replace("call_version_1",
                    "call_version_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))))
                : Proposal(VersionProposal.Replace("call_version_1", "call_version_7", StringComparison.Ordinal)));
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var receipt = await c.Conversation.SubmitClaudeObservationsAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "original", [ReadOnlyCapabilityCatalog.Version], () => true, f.Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Boundary.VersionReads.Should().Be(6);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(4);
        using var continuation = JsonDocument.Parse(c.Http.Requests.Last(item => item.Method is "POST").Body);
        var history = continuation.RootElement.GetProperty("messages");
        history.GetArrayLength().Should().Be(3);
        history[1].GetProperty("content").GetArrayLength().Should().Be(6);
        history[2].GetProperty("content").GetArrayLength().Should().Be(6);
        history[2].GetProperty("content").EnumerateArray().Select(result => result.GetProperty("tool_use_id").GetString())
            .Should().Equal(Enumerable.Range(1, 6).Select(index => "call_version_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        c.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task HostDeadlineFencesNativeLateAnswerAndKeepsPhysicalHttpOwnedUntilJoined()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f); await c.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Http.Send = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return Task.FromResult(ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":25}""")); }
            entered.TrySetResult(); return release.Task;
        };
        c.Reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        var run = c.Conversation.SubmitClaudeObservationsAsync(await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token),
            "original", [ReadOnlyCapabilityCatalog.Version], () => true, f.Token);
        await entered.Task.WaitAsync(f.Token);
        c.TurnTime.FireDeadline();
        c.Runtime.IsQuiescent.Should().BeFalse();
        c.Turns.IsQuiescent.Should().BeFalse();
        run.IsCompleted.Should().BeFalse();
        release.SetResult(ClaudeAccountBrokerTests.Fixture.Json(ClaudeMessagesRuntimeTests.Answer));
        var receipt = await run;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.AssistantResponse.Should().BeNull();
        c.Boundary.VersionReads.Should().Be(0);
        c.Http.Requests.Count(item => item.Method is "POST").Should().Be(2);
        c.Runtime.IsQuiescent.Should().BeTrue(); c.Turns.IsQuiescent.Should().BeTrue();
    }
}
