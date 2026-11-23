using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class AgentFrameworkExecutionTests : IDisposable
{
    private const string Answer = """{"kind":"answer","answer":"Exact final answer."}""";
    private const string Call = """{"kind":"toolCalls","calls":[{"callId":"original-one","capabilityId":"application.get_version","input":{}}]}""";
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public AgentFrameworkExecutionTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PackageExecutesSerialCallbacksWithOriginalIdsAndImmutableGuidance(int count)
    {
        await using var f = new Fixture(count, count + 1);
        f.Transport.Messages = count == 0 ? [Answer] : count == 1 ? [Call, Answer] :
            [Call.Replace("]}", """,{"callId":"original-two","capabilityId":"application.get_version","input":{}}]}""",
                StringComparison.Ordinal), Answer];
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be("Exact final answer.");
        f.Invocations.Should().Equal(Enumerable.Range(1, count).Select(index => index == 1 ? "original-one" : "original-two"));
        f.PackageInvocations.Should().Be(count);
        f.Transport.Requests.Should().HaveCount(count == 0 ? 1 : 2);
        f.Transport.Requests.Should().OnlyContain(request => request.Context.SystemPolicy == "Exact captured agent instructions"
            && request.Context.Evidence.Single().Content == "Already-consented shared skill guidance"
            && request.Context.Evidence.Single().Disclosure == ModelEvidenceDisclosure.LocalOnly
            && request.Context.Tools.Count == (count == 0 ? 0 : 1));
        if (count != 0)
        {
            f.Transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Select(message => message.CallId)
                .Should().Equal(f.Invocations);
            f.Transport.Requests[1].Budget.InputUtf8Bytes.Should().BeLessThan(32768);
            f.Transport.Requests[1].Budget.OutputUtf8Bytes.Should().BeLessThan(4096);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task CompatibleBoundedProvidersAndConstraintVersionsUseMaintainedSerialExecution(bool local, int version)
    {
        await using var f = new Fixture(1, 2, local: local, version: version);
        f.Transport.Messages = [Call, Answer];
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be("Exact final answer.");
        f.PackageInvocations.Should().Be(1);
        f.Invocations.Should().Equal("original-one");
        f.Transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Single().CallId.Should().Be("original-one");
        f.Transport.Requests[0].Provenance.Selection.Provider.Should()
            .Be(local ? ModelProviderIdentity.Ollama : ModelProviderIdentity.Copilot);
    }

    [Theory]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"missing","input":{}}]}""", ModelTurnReason.InvalidProviderResponse)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"runtime.list","input":{}}]}""", ModelTurnReason.ToolUseNotPermitted)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"one","capabilityId":"application.get_version","input":{}}]}""", ModelTurnReason.InvalidProviderResponse)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"","capabilityId":"application.get_version","input":{}}]}""", ModelTurnReason.InvalidProviderResponse)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{"unknown":true}}]}""", ModelTurnReason.InvalidProviderResponse)]
    public async Task InvalidWholeBatchHasNoAllowedPrefixEffect(string message, ModelTurnReason reason)
    {
        await using var f = new Fixture(2, 3);
        f.Transport.Messages = [message];
        var result = await f.Run();
        result.Reason.Should().Be(reason);
        result.Response.Should().BeNull();
        f.Invocations.Should().BeEmpty();
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CallsCannotExceedZeroOrNonzeroHostLimits(int limit)
    {
        await using var f = new Fixture(limit, limit + 1);
        f.Transport.Messages = [Call.Replace("]}", """,{"callId":"two","capabilityId":"application.get_version","input":{}}]}""",
            StringComparison.Ordinal)];
        var result = await f.Run();
        result.Reason.Should().Be(limit == 0 ? ModelTurnReason.ToolUseNotPermitted : ModelTurnReason.ToolLoopBudgetExceeded);
        f.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateIdsAcrossPackageIterationsCannotInvokeTwice()
    {
        await using var f = new Fixture(2, 3);
        f.Transport.Messages = [Call, Call];
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Invocations.Should().Equal("original-one");
        f.Transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("round")]
    [InlineData("input-exhausted")]
    [InlineData("output-exhausted")]
    [InlineData("remaining-input")]
    public async Task HostBudgetsStopPackageContinuationWithoutAnotherTransport(string fault)
    {
        await using var f = new Fixture(1, fault is "round" ? 1 : 2);
        f.Transport.Messages = [Call, Answer];
        f.Transport.Transform = (request, reply) => reply with
        {
            Usage = fault switch
            {
                "input-exhausted" => reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes },
                "output-exhausted" => reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes },
                "remaining-input" => reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes - 1 },
                _ => reply.Usage,
            },
        };
        var result = await f.Run();
        result.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
        result.Response.Should().BeNull();
        f.Transport.Requests.Should().HaveCount(1);
        f.Invocations.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("usage")]
    [InlineData("under-input")]
    [InlineData("under-output")]
    [InlineData("over-input")]
    [InlineData("over-output")]
    [InlineData("provenance")]
    [InlineData("outcome")]
    [InlineData("message")]
    public async Task InvalidTransportReportCannotProduceSuccess(string fault)
    {
        await using var f = new Fixture(0, 1);
        f.Transport.Transform = (request, reply) => fault switch
        {
            "usage" => reply with { Usage = null },
            "under-input" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = 0 } },
            "under-output" => reply with { Usage = reply.Usage! with { OutputUtf8Bytes = 0 } },
            "over-input" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes + 1 } },
            "over-output" => reply with { Usage = reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes + 1 } },
            "provenance" => reply with { Provenance = reply.Provenance with { Turn = new(Guid.NewGuid()) } },
            "outcome" => reply with { Outcome = (ModelTurnOutcome)int.MaxValue },
            _ => reply with { Message = null },
        };
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        f.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task ProviderFailureRetainsItsOutcomeAndTokenReport(ModelTurnOutcome outcome)
    {
        await using var f = new Fixture(0, 1);
        f.Transport.Transform = (_, reply) => reply with { Outcome = outcome, Message = null, TokenUsage = new(7, 3) };
        var result = await f.Run();
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().Be(ModelTurnReason.ProviderOutcome);
        result.TokenUsage.Should().Be(new ModelTokenUsage(7, 3));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("before-callback")]
    [InlineData("after-callback")]
    public async Task RevocationAtEveryAwaitSuppressesCallbacksAndFinalAnswer(string boundary)
    {
        await using var f = new Fixture(1, 2);
        f.Transport.Messages = [Call, Answer];
        var afterModelChecks = 0;
        f.Validate = () => boundary is "model" && f.Transport.Requests.Count != 0
            || boundary is "before-callback" && f.Transport.Requests.Count != 0 && ++afterModelChecks == 2
            || boundary is "after-callback" && f.Invocations.Count != 0
                ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None;
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        f.Invocations.Should().HaveCount(boundary is "after-callback" ? 1 : 0);
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactProfileDeadlineAndBackwardClockStopTheAgent(bool backward)
    {
        await using var f = new Fixture(1, 2, deadline: 1);
        f.Transport.Messages = [Call];
        f.Transport.Transform = (_, reply) =>
        {
            f.Time.Now += backward ? TimeSpan.FromTicks(-1) : TimeSpan.FromMilliseconds(1);
            return reply;
        };
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        result.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        f.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CapabilityOutcome.Denied, ModelTurnOutcome.Denied)]
    [InlineData(CapabilityOutcome.Unavailable, ModelTurnOutcome.Unavailable)]
    [InlineData(CapabilityOutcome.Failed, ModelTurnOutcome.Unknown)]
    public async Task ToolFailureStopsThePackageWithoutRecoveryOrAnotherModelCall(
        CapabilityOutcome toolOutcome, ModelTurnOutcome outcome)
    {
        await using var f = new Fixture(1, 2);
        f.Transport.Messages = [Call];
        f.Tool = _ => ValueTask.FromResult(new CapabilityReply(toolOutcome, "not observed"));
        var result = await f.Run();
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
        result.Response.Should().BeNull();
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrWrongToolResultCannotEnterPackageHistory(bool missing)
    {
        await using var f = new Fixture(1, 2);
        f.Transport.Messages = [Call];
        f.Tool = _ => ValueTask.FromResult(missing ? null! : new CapabilityReply(CapabilityOutcome.Succeeded, "missing observation"));
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("overflow")]
    public async Task AgentTokenTotalsRemainTruthfulAcrossPackageCalls(string report)
    {
        await using var f = new Fixture(1, 2);
        f.Transport.Messages = [Call, Answer];
        f.Transport.Transform = (request, reply) => reply with
        {
            TokenUsage = request.Round == 1 ? new(report is "overflow" ? long.MaxValue : 10, 2, 10, 100)
                : report switch
                {
                    "missing" => null,
                    "negative" => new(-1, 4, 30, 200),
                    _ => new(30, 4, 30, 200),
                },
        };
        var result = await f.Run();
        result.Outcome.Should().Be(report is "negative" or "overflow" ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Succeeded);
        result.TokenUsage.Should().Be(report switch
        {
            "complete" => new ModelTokenUsage(40, 6, 30, 200),
            "missing" => new ModelTokenUsage(),
            _ => new ModelTokenUsage(),
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoncooperativeModelOrCallbackStaysOwnedUntilPhysicallyJoined(bool inCallback)
    {
        await using var f = new Fixture(1, 2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Messages = [inCallback ? Call : Answer];
        if (inCallback)
        {
            f.Tool = async _ => { entered.SetResult(); await release.Task; return Fixture.Version(); };
        }
        else
        {
            f.Transport.BeforeReply = async () => { entered.SetResult(); await release.Task; };
        }
        var running = f.Run(cancellation.Token);
        await entered.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        var disposal = f.Adapter.DisposeAsync().AsTask();
        running.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse();
        f.Adapter.IsQuiescent.Should().BeFalse();
        f.Transport.Disposed.Should().BeFalse();
        release.SetResult();
        var result = await running;
        await disposal;
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        result.Response.Should().BeNull();
        f.Transport.Disposed.Should().BeTrue();
        f.Adapter.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("question")]
    [InlineData("invalid-first-tokens")]
    public async Task InvalidProviderShapeCannotBeAdaptedIntoAPackageAnswer(string fault)
    {
        await using var f = new Fixture(0, 1);
        f.Transport.Transform = (_, reply) => fault switch
        {
            "null" => null!,
            "question" => reply with
            {
                Message = ModelToolProtocol.ParseAssistant(
                    Encoding.UTF8.GetBytes("""{"kind":"question","prompt":"Choose","options":["A","B"]}"""), clarificationAllowed: true),
            },
            _ => reply with { TokenUsage = new(-1, 0) },
        };
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.TokenUsage.Should().BeNull();
    }

    [Fact]
    public async Task RevocationAfterPackageCompletionStillSuppressesTheFinalAnswer()
    {
        await using var f = new Fixture(0, 1);
        var checksAfterModel = 0;
        f.Validate = () => f.Transport.Requests.Count != 0 && ++checksAfterModel == 2
            ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None;
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
    }

    [Theory]
    [InlineData("null-options")]
    [InlineData("null-tools")]
    [InlineData("extra-tools")]
    [InlineData("substituted-tools")]
    [InlineData("pending")]
    [InlineData("pending-question")]
    [InlineData("final")]
    public async Task PackageCannotSubstituteFunctionsOrRequestAnotherUnfinishedOrTerminalExchange(string fault)
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge(ask: fault is "pending-question"
            ? (question, _) => Task.FromResult(new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.LocalUi))
            : null);
        var seed = new ChatMessage(ChatRole.User, "Original task");
        var options = new ChatOptions { Tools = bridge.Tools };
        List<ChatMessage> messages = [seed];
        if (fault is "pending" or "pending-question" or "final")
        {
            f.Transport.Messages = [fault is "pending" ? Call : Answer];
            if (fault is "pending-question") { SetQuestion(f, correlated: true); }
            var response = await bridge.GetResponseAsync(messages, options, Token);
            if (fault is "pending" or "pending-question") { messages.AddRange(response.Messages); }
        }
        else
        {
            options = fault switch
            {
                "null-options" => null,
                "null-tools" => new(),
                "extra-tools" => new() { Tools = [] },
                _ => new() { Tools = [AIFunctionFactory.Create(() => "unapproved", name: "application.get_version")] },
            };
        }
        var act = () => bridge.GetResponseAsync(messages, options, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Transport.Requests.Should().HaveCount(fault is "pending" or "pending-question" or "final" ? 1 : 0);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("text")]
    [InlineData("extra")]
    public async Task PackageCannotReplaceTheOriginalUserMessage(string fault)
    {
        await using var f = new Fixture(0, 1);
        using var bridge = f.CreateBridge();
        List<ChatMessage> messages = fault switch
        {
            "empty" => [],
            "role" => [new(ChatRole.System, "Original task")],
            "type" => [new(ChatRole.User, [new FunctionResultContent("wrong", "data")])],
            "text" => [new(ChatRole.User, "changed")],
            _ => [new(ChatRole.User, "Original task"), new(ChatRole.User, "extra")],
        };
        var act = () => bridge.GetResponseAsync(messages, new() { Tools = bridge.Tools }, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("arguments")]
    public async Task PackageAssistantHistoryMustExactlyMatchTheValidatedBatch(string fault)
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge();
        f.Transport.Messages = [Call];
        var seed = new ChatMessage(ChatRole.User, "Original task");
        var options = new ChatOptions { Tools = bridge.Tools };
        var response = await bridge.GetResponseAsync([seed], options, Token);
        var message = response.Messages.Single();
        var call = (FunctionCallContent)message.Contents.Single();
        if (fault is "role") { message.Role = ChatRole.Tool; }
        if (fault is "type") { message.Contents[0] = new TextContent("changed"); }
        if (fault is "id" or "name" or "arguments")
        {
            message.Contents[0] = new FunctionCallContent(fault is "id" ? "wrong" : call.CallId,
                fault is "name" ? "runtime.list" : call.Name, fault is "arguments" ? null : call.Arguments);
        }
        var act = () => bridge.GetResponseAsync(fault is "missing" ? [seed] : [seed, message], options, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("no-pending")]
    [InlineData("no-context")]
    [InlineData("function")]
    [InlineData("name")]
    [InlineData("id")]
    [InlineData("arguments")]
    public async Task FunctionInvocationRequiresTheOriginalPackageCorrelationAndValidatedInput(string fault)
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge();
        f.Transport.Messages = [Call];
        var function = (AIFunction)bridge.Tools.Single();
        if (fault is "no-pending" or "no-context")
        {
            if (fault is "no-context")
            {
                await bridge.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
            }
            Func<Task> act = async () => await function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), Token);
            (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
                .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        }
        else
        {
            using var functions = new FunctionInvokingChatClient(bridge) { MaximumConsecutiveErrorsPerRequest = 0 };
            functions.FunctionInvoker = (invocation, token) =>
            {
                if (fault is "function") { invocation.Function = AIFunctionFactory.Create(() => "wrong"); }
                if (fault is "name") { invocation.CallContent = new(invocation.CallContent.CallId, "runtime.list", invocation.CallContent.Arguments); }
                if (fault is "id") { invocation.CallContent = new("wrong", invocation.CallContent.Name, invocation.CallContent.Arguments); }
                var arguments = new AIFunctionArguments(invocation.Arguments, StringComparer.Ordinal);
                if (fault is "arguments") { arguments["unknown"] = true; }
                return function.InvokeAsync(arguments, token);
            };
            var act = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
            (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
                .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        }
        f.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("id")]
    [InlineData("result")]
    [InlineData("exception")]
    public async Task PackageToolHistoryCannotReplaceOrMisattributeTheHostResult(string fault)
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge();
        f.Transport.Messages = [Call];
        using var mutating = new MutatingChatClient(bridge, messages =>
        {
            var message = messages.FirstOrDefault(message => message.Role == ChatRole.Tool);
            if (message is null) { return; }
            if (fault is "missing") { messages.Remove(message); return; }
            var original = (FunctionResultContent)message.Contents.Single();
            var content = new FunctionResultContent(fault is "id" ? "wrong" : original.CallId,
                fault is "result" ? "fabricated" : original.Result)
            { Exception = fault is "exception" ? new InvalidOperationException("not a result") : null };
            if (fault is "role") { message.Role = ChatRole.Assistant; }
            message.Contents[0] = fault is "type" ? new TextContent("changed") : content;
        });
        using var functions = new FunctionInvokingChatClient(mutating) { MaximumConsecutiveErrorsPerRequest = 0 };
        var act = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Transport.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("no-final")]
    [InlineData("pending-question")]
    [InlineData("empty")]
    [InlineData("role")]
    [InlineData("contents")]
    [InlineData("type")]
    [InlineData("text")]
    public async Task PackageCannotClaimATerminalAnswerWithoutTheExactAdmittedFinalMessage(string fault)
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge(ask: fault is "pending-question"
            ? (question, _) => Task.FromResult(new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.LocalUi))
            : null);
        var messages = new List<ChatMessage>();
        if (fault is not "no-final")
        {
            f.Transport.Messages = [fault is "pending" ? Call : Answer];
            if (fault is "pending-question") { SetQuestion(f, correlated: true); }
            var response = await bridge.GetResponseAsync([new(ChatRole.User, "Original task")],
                new() { Tools = bridge.Tools }, Token);
            messages.AddRange(response.Messages);
        }
        if (fault is "empty") { messages.Clear(); }
        if (fault is "role") { messages[0].Role = ChatRole.User; }
        if (fault is "contents") { messages[0].Contents.Clear(); }
        if (fault is "type") { messages[0].Contents[0] = new FunctionResultContent("wrong", "data"); }
        if (fault is "text") { messages[0].Contents[0] = new TextContent("fabricated"); }
        var act = () => bridge.ReadFinalAnswer(messages);
        act.Should().Throw<AgentFrameworkModelChatClient.TurnStoppedException>();
    }

    [Fact]
    public async Task BridgeExposesOnlyItsOwnChatServiceAndCanonicalSchemasAndRefusesStreaming()
    {
        await using var f = new Fixture(1, 2);
        using var bridge = f.CreateBridge();
        bridge.GetService(typeof(IChatClient)).Should().BeSameAs(bridge);
        bridge.GetService(typeof(IModelToolTransport)).Should().BeNull();
        bridge.GetService(typeof(IChatClient), "foreign").Should().BeNull();
        var function = (AIFunction)bridge.Tools.Single();
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        { CapabilityInputSchema.Write(writer, ReadOnlyCapabilityCatalog.FindExact(function.Name)!); }
        using var expected = System.Text.Json.JsonDocument.Parse(stream.ToArray());
        System.Text.Json.JsonElement.DeepEquals(expected.RootElement, function.JsonSchema).Should().BeTrue();
        var act = () => bridge.GetStreamingResponseAsync([], cancellationToken: Token);
        act.Should().Throw<NotSupportedException>();
        f.Transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolFreeBridgeStreamsOnlyValidatedPrefixesAndJoinsEachPublication(bool replacePrefix)
    {
        await using var f = new Fixture(0, 1, version: 2);
        var published = string.Empty;
        var transport = new StreamingTransport(() => published, replacePrefix);
        using var bridge = f.CreateBridge(transport);
        var updates = bridge.GetStreamingResponseAsync([new(ChatRole.User, "Original task")],
            new() { Tools = bridge.Tools }, Token);
        var consume = async () =>
        {
            await foreach (var update in updates)
            { published += update.Text; }
        };
        if (replacePrefix)
        {
            await consume.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>();
            published.Should().Be("Exact ");
        }
        else
        {
            await consume();
            published.Should().Be("Exact final answer.");
            bridge.ReadFinalAnswer([new(ChatRole.Assistant, published)])
                .Should().Be(published);
        }
        transport.PublicationJoined.Should().BeTrue();
        var late = async () => await transport.LatePublication!();
        await late.Should().ThrowAsync<InvalidDataException>();
        published.Should().Be(replacePrefix ? "Exact " : "Exact final answer.");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("foreign-question")]
    [InlineData("budget")]
    [InlineData("host-fault")]
    public async Task QuestionAnswerRequiresExactHostQuestionAndRemainingBudget(string fault)
    {
        await using var f = new Fixture(1, 2);
        SetQuestion(f, correlated: true, exhaustOutput: fault is "budget");
        var asks = 0;
        using var bridge = f.CreateBridge(ask: (question, _) =>
        {
            asks++;
            if (fault is "host-fault") { throw new IOException("Synthetic question host failure."); }
            return Task.FromResult(fault is "null" ? null! : new ModelQuestionAnswerMessage(
                fault is "foreign-question" ? new(question.Question, question.CallId) : question,
                new(["option-1"]), RequestOrigin.LocalUi));
        });
        using var functions = new ModelChatMessageProjection.QuestionContinuationClient(bridge)
        { MaximumConsecutiveErrorsPerRequest = 0, AdditionalTools = bridge.QuestionTools };
        var act = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(fault is "budget" ? ModelTurnReason.ToolLoopBudgetExceeded
                : fault is "host-fault" ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.InvalidProviderResponse);
        asks.Should().Be(1);
        f.Transport.Requests.Should().ContainSingle();
        f.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "missing-question")]
    [InlineData(false, "question-role")]
    [InlineData(false, "question-type")]
    [InlineData(false, "question-text")]
    [InlineData(false, "missing-call")]
    [InlineData(false, "call-role")]
    [InlineData(false, "call-type")]
    [InlineData(false, "call-id")]
    [InlineData(false, "call-name")]
    [InlineData(false, "call-arguments")]
    [InlineData(false, "missing-answer")]
    [InlineData(false, "answer-role")]
    [InlineData(false, "answer-type")]
    [InlineData(false, "answer-text")]
    [InlineData(true, "missing-answer")]
    [InlineData(true, "missing-call")]
    [InlineData(true, "call-role")]
    [InlineData(true, "call-type")]
    [InlineData(true, "call-id")]
    [InlineData(true, "call-name")]
    [InlineData(true, "call-arguments")]
    [InlineData(true, "answer-role")]
    [InlineData(true, "answer-type")]
    [InlineData(true, "answer-id")]
    [InlineData(true, "answer-result")]
    [InlineData(true, "answer-exception")]
    public async Task QuestionTranscriptCannotReplaceQuestionOrHostAnswer(bool correlated, string fault)
    {
        await using var f = new Fixture(1, 2);
        SetQuestion(f, correlated);
        var asks = 0;
        using var bridge = f.CreateBridge(ask: (question, _) =>
        {
            asks++;
            return Task.FromResult(new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.LocalUi));
        });
        using var mutating = new MutatingChatClient(bridge, messages =>
        {
            if (messages.Count < 3) { return; }
            var index = fault.Contains("question", StringComparison.Ordinal)
                || fault.StartsWith("call-", StringComparison.Ordinal) || fault is "missing-call" ? 1 : messages.Count - 1;
            var message = messages[index];
            if (fault is "missing-call") { message.Contents.RemoveAt(message.Contents.Count - 1); return; }
            if (fault.StartsWith("missing-", StringComparison.Ordinal)) { messages.RemoveAt(index); return; }
            if (fault.EndsWith("-role", StringComparison.Ordinal)) { message.Role = ChatRole.System; return; }
            if (fault.EndsWith("-type", StringComparison.Ordinal))
            {
                message.Contents[fault is "call-type" ? message.Contents.Count - 1 : 0] = fault is "call-type"
                    ? new TextContent("substituted") : new FunctionCallContent("foreign", "foreign");
                return;
            }
            if (fault.EndsWith("-text", StringComparison.Ordinal)) { message.Contents[0] = new TextContent("substituted"); return; }
            if (fault.StartsWith("call-", StringComparison.Ordinal))
            {
                var call = (FunctionCallContent)message.Contents[^1];
                message.Contents[^1] = new FunctionCallContent(fault is "call-id" ? "foreign" : call.CallId,
                    fault is "call-name" ? "foreign" : call.Name, fault is "call-arguments" ? null : call.Arguments);
            }
            else
            {
                var result = (FunctionResultContent)message.Contents[0];
                message.Contents[0] = new FunctionResultContent(fault is "answer-id" ? "foreign" : result.CallId,
                    fault is "answer-result" ? "substituted" : result.Result)
                { Exception = fault is "answer-exception" ? new IOException("Synthetic result fault.") : null };
            }
        });
        using var functions = new ModelChatMessageProjection.QuestionContinuationClient(mutating)
        { MaximumConsecutiveErrorsPerRequest = 0, AdditionalTools = bridge.QuestionTools };
        var act = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
        (await act.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>())
            .Which.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        asks.Should().Be(1);
        f.Transport.Requests.Should().ContainSingle();
        f.Invocations.Should().BeEmpty();
    }

    private static void SetQuestion(Fixture fixture, bool correlated, bool exhaustOutput = false)
    {
        fixture.Transport.Messages = [Answer, Answer];
        fixture.Transport.Transform = (request, reply) =>
        {
            if (request.Round != 1) { return reply; }
            var question = new ModelClarificationQuestion(new("Choose", ["A", "B"]), correlated ? "question-one" : null);
            return reply with { Message = question, Usage = reply.Usage! with
            { OutputUtf8Bytes = exhaustOutput ? request.Budget.OutputUtf8Bytes : ModelToolProtocol.SerializeMessage(question).Length } };
        };
    }

    [Theory]
    [InlineData("no-pending")]
    [InlineData("no-context")]
    [InlineData("function")]
    [InlineData("count")]
    [InlineData("index")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("arguments")]
    public async Task QuestionInvocationRequiresOriginalSerialPackageCorrelation(string fault)
    {
        await using var f = new Fixture(1, 2);
        SetQuestion(f, correlated: true);
        var asks = 0;
        using var bridge = f.CreateBridge(ask: (question, _) =>
        {
            asks++;
            return Task.FromResult(new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.LocalUi));
        });
        var function = (AIFunction)bridge.QuestionTools.Single();
        if (fault is "no-pending" or "no-context")
        {
            if (fault is "no-context")
            { await bridge.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token); }
            Func<Task> invoke = async () => await function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), Token);
            await invoke.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>();
        }
        else
        {
            using var functions = new ModelChatMessageProjection.QuestionContinuationClient(bridge)
            { MaximumConsecutiveErrorsPerRequest = 0, AdditionalTools = bridge.QuestionTools };
            functions.FunctionInvoker = (invocation, token) =>
            {
                if (fault is "function") { invocation.Function = AIFunctionFactory.Create(() => "foreign"); }
                if (fault is "count") { invocation.FunctionCount = 2; }
                if (fault is "index") { invocation.FunctionCallIndex = 1; }
                if (fault is "id" or "name")
                { invocation.CallContent = new(fault is "id" ? "foreign" : invocation.CallContent.CallId,
                    fault is "name" ? "foreign" : invocation.CallContent.Name, invocation.CallContent.Arguments); }
                var arguments = new AIFunctionArguments(invocation.Arguments, StringComparer.Ordinal);
                if (fault is "arguments") { arguments["foreign"] = true; }
                return function.InvokeAsync(arguments, token);
            };
            var invoke = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
            await invoke.Should().ThrowAsync<AgentFrameworkModelChatClient.TurnStoppedException>();
        }
        asks.Should().Be(0);
        f.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledQuestionHostDoesNotContinueModelOrInvokeTools()
    {
        await using var f = new Fixture(1, 2);
        SetQuestion(f, correlated: true);
        using var bridge = f.CreateBridge(ask: (_, _) => throw new OperationCanceledException());
        using var functions = new ModelChatMessageProjection.QuestionContinuationClient(bridge)
        { MaximumConsecutiveErrorsPerRequest = 0, AdditionalTools = bridge.QuestionTools };
        var invoke = () => functions.GetResponseAsync([new(ChatRole.User, "Original task")], new() { Tools = bridge.Tools }, Token);
        await invoke.Should().ThrowAsync<OperationCanceledException>();
        f.Transport.Requests.Should().ContainSingle();
        f.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("tools")]
    [InlineData("provider")]
    [InlineData("transport")]
    public async Task StreamingRequiresLocalToolFreeTransportWithoutQuestionOwner(string fault)
    {
        await using var f = new Fixture(fault is "tools" ? 1 : 0, fault is "tools" ? 2 : 1,
            local: fault is not "provider", version: 2);
        using var bridge = f.CreateBridge(fault is "transport" ? null : new StreamingTransport(() => "", false),
            ask: fault is "question" ? (_, _) => throw new InvalidOperationException("No question may open.") : null);
        var invoke = () => bridge.GetStreamingResponseAsync([new(ChatRole.User, "Original task")],
            new() { Tools = bridge.Tools }, Token);
        invoke.Should().Throw<NotSupportedException>();
        f.Transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingOrCancellingStreamJoinsTransportWithoutAcceptingLaterPublication(bool cancel)
    {
        await using var f = new Fixture(0, 1, version: 2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var transport = new StreamingTransport(() => "", false);
        using var bridge = f.CreateBridge(transport);
        var enumerator = bridge.GetStreamingResponseAsync([new(ChatRole.User, "Original task")],
            new() { Tools = bridge.Tools }, cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Text.Should().Be("Exact ");
        if (cancel) { await cancellation.CancelAsync(); }
        if (cancel)
        {
            Func<Task> dispose = () => enumerator.DisposeAsync().AsTask();
            await dispose.Should().ThrowAsync<OperationCanceledException>();
        }
        else { await enumerator.DisposeAsync(); }
        var late = async () => await transport.LatePublication!();
        if (cancel) { await late.Should().ThrowAsync<OperationCanceledException>(); }
        else { await late.Should().ThrowAsync<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonAnswerPackageUpdatesNeverReachTheHostPublisher(bool foreignRole)
    {
        await using var f = new Fixture(0, 1);
        using var bridge = f.CreateBridge();
        var published = new List<ModelAnswerUpdate>();
        var update = new Microsoft.Agents.AI.AgentResponseUpdate
        {
            Role = foreignRole ? ChatRole.User : ChatRole.Assistant,
            Contents = foreignRole ? [new TextContent("foreign")] : [new FunctionCallContent("foreign", "foreign")],
        };
        async IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate> Updates()
        {
            await Task.CompletedTask;
            yield return update;
        }
        Func<Task> consume = async () =>
        {
            await foreach (var _ in BoundedModelTurnAdapter.PublishAsync(Updates(), f.Provenance,
                (answer, _) => { published.Add(answer); return ValueTask.CompletedTask; }, bridge, Token)) { }
        };
        await consume.Should().ThrowAsync<InvalidDataException>();
        published.Should().BeEmpty();
        f.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamDisposalSurfacesTransportJoinFailureInsteadOfReportingCleanCancellation()
    {
        await using var f = new Fixture(0, 1, version: 2);
        var transport = new StreamingTransport(() => "", false, failOnJoin: true);
        using var bridge = f.CreateBridge(transport);
        var enumerator = bridge.GetStreamingResponseAsync([new(ChatRole.User, "Original task")],
            new() { Tools = bridge.Tools }, Token).GetAsyncEnumerator(Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        Func<Task> dispose = () => enumerator.DisposeAsync().AsTask();
        await dispose.Should().ThrowAsync<IOException>().WithMessage("Synthetic transport join failure.");
    }

    [Fact]
    public async Task TransportCancellationDuringEnumerationPreservesAcceptedPrefixAndRemainsAFailure()
    {
        await using var f = new Fixture(0, 1, version: 2);
        var published = string.Empty;
        var transport = new StreamingTransport(() => published, false, cancelBeforeReply: true);
        using var bridge = f.CreateBridge(transport);
        Func<Task> consume = async () =>
        {
            await foreach (var update in bridge.GetStreamingResponseAsync([new(ChatRole.User, "Original task")],
                new() { Tools = bridge.Tools }, Token))
            { published += update.Text; }
        };
        await consume.Should().ThrowAsync<OperationCanceledException>();
        published.Should().Be("Exact ");
        var late = async () => await transport.LatePublication!();
        await late.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly Clock Time = new();
        internal readonly Transport Transport = new();
        internal readonly List<string> Invocations = [];
        internal readonly BoundedModelTurnAdapter Adapter;
        internal readonly ModelTurnProvenance Provenance;
        private readonly ModelContextEnvelope envelope;
        internal int PackageInvocations { get; private set; }
        internal Func<ModelTurnReason> Validate { get; set; } = () => ModelTurnReason.None;
        internal Func<CancellationToken, ValueTask<CapabilityReply>> Tool { get; set; } = _ => ValueTask.FromResult(Version());

        internal Fixture(int calls, int rounds, int deadline = 15000, bool local = true, int version = 1)
        {
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            Provenance = new(new(Guid.NewGuid()), request, local ? ModelProviderSelection.OllamaCandidate : ModelProviderSelection.CopilotCandidate);
            envelope = new(request, "Exact captured agent instructions", "Original task",
                [new(new(Guid.NewGuid()), request, new(1), "Already-consented shared skill guidance", ModelEvidenceDisclosure.LocalOnly)],
                Time.Now, Time.Now.AddMinutes(1), calls == 0 ? ModelToolMode.None : ModelToolMode.ReadOnly,
                new(version, calls == 0 ? [] : [ReadOnlyCapabilityCatalog.Version], calls, rounds, 32768, 4096, deadline));
            Adapter = new(Transport, Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        }

        internal async Task<ModelProviderReply> Run(CancellationToken? cancellationToken = null)
        {
            using var root = HostActivity.BeginRoot(Provenance.Request, HostActivityLayer.Application, HostOperation.Request);
            return await Adapter.RunAsync(Provenance, null, envelope.Serialize(), async (id, input, token) =>
            {
                var context = FunctionInvokingChatClient.CurrentContext;
                if (context is not null)
                {
                    PackageInvocations++;
                    context.CallContent.Name.Should().Be(id);
                    Invocations.Add(context.CallContent.CallId);
                }
                else { Invocations.Add("not a package callback"); }
                input.Should().Be("{}");
                return await Tool(token);
            }, _ => ValueTask.FromResult(Validate()), cancellationToken ?? Token);
        }

        internal static CapabilityReply Version() => new(CapabilityOutcome.Succeeded, "observed", Version: new("1", "not observed"));
        internal AgentFrameworkModelChatClient CreateBridge(IModelToolTransport? transport = null,
            Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask = null) => new(transport ?? Transport, Provenance, null,
            ModelToolProtocol.ReadContext(envelope.Serialize(), Provenance), (_, _, token) => Tool(token),
            _ => ValueTask.FromResult<ModelProviderReply?>(null), () => { }, _ => { }, (_, _, _, _) => { }, ask);
        public ValueTask DisposeAsync() => Adapter.DisposeAsync();
    }

    private sealed class StreamingTransport(Func<string> published, bool replacePrefix, bool failOnJoin = false,
        bool cancelBeforeReply = false) : IModelToolTransport, IModelAnswerTransport
    {
        internal bool PublicationJoined { get; private set; }
        internal Func<ValueTask>? LatePublication { get; private set; }
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This regression requires the real streamed transport seam.");

        public async Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            LatePublication = () => publish(new(request.Provenance, "Exact final answer. Late"), CancellationToken.None);
            try { await publish(new(request.Provenance, "Exact "), cancellationToken); }
            catch (OperationCanceledException exception) when (failOnJoin)
            { throw new IOException("Synthetic transport join failure.", exception); }
            PublicationJoined = string.Equals(published(), "Exact ", StringComparison.Ordinal);
            if (cancelBeforeReply) { throw new OperationCanceledException("Synthetic transport cancellation."); }
            await publish(new(request.Provenance, replacePrefix ? "Replacement" : "Exact final answer."), cancellationToken);
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(Answer));
            return new(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MutatingChatClient(IChatClient inner, Action<IList<ChatMessage>> mutate) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var captured = messages.ToList();
            mutate(captured);
            return base.GetResponseAsync(captured, options, cancellationToken);
        }
    }

    private sealed class Transport : IModelToolTransport
    {
        internal string[] Messages { get; set; } = [Answer];
        internal List<ModelToolRequest> Requests { get; } = [];
        internal Func<ModelToolRequest, ModelToolTransportReply, ModelToolTransportReply>? Transform { get; set; }
        internal Func<Task>? BeforeReply { get; set; }
        internal bool Disposed { get; private set; }

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (BeforeReply is not null) { await BeforeReply(); }
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(Messages[Requests.Count - 1]));
            var reply = new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
            return Transform is null ? reply : Transform(request, reply);
        }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
