using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("foreign")]
    [InlineData("closed-source")]
    [InlineData("expired")]
    [InlineData("generation")]
    [InlineData("cancelled")]
    [InlineData("stopped-context")]
    public async Task ExecutionConsumerPortPreservesExistingHostAuthorityAndSingleUseFences(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        IModelTurnExecution execution = fault is "foreign" ? f.Qualified(ModelProviderSelection.OllamaCandidate) : f.Host;
        if (fault is "expired") { f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(1); }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "stopped-context") { root.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { cancellation.Cancel(); }
        var response = await execution.RunAsync(turn, () => fault is not "closed-source", cancellation.Token);
        response.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        response.Response.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        (await execution.RunAsync(turn, static () => true, Token)).Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AsynchronousOriginalSourceValidationFailureCannotInvokeAdapterAndDoesNotLeakBody()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        IModelTurnExecution execution = f.Host;
        await execution.Invoking(port => port.RunAsync(turn, static () => true, Token,
            _ => throw new IOException("private source body must not be logged")))
            .Should().ThrowAsync<IOException>();
        f.Adapter.Calls.Should().Be(0);
        f.Messages.Should().NotContain(text => text.Contains("private source body", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task OriginalRetainedRequestExecutesExactlyOnceThroughRealHostToolCallbackAndReturnsRealAnswerAndExactReadback(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var service = f.ConversationService(host, execution: host);
        const string body = "What is the actual application version?";
        var tools = 0;
        var validations = 0;
        f.Adapter.OnTool = async invoke =>
        {
            var observed = await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token);
            observed.Outcome.Should().Be(CapabilityOutcome.Succeeded);
            tools++;
            f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new("The admitted host reported " + observed.Version!.Version + ".", null));
        };
        var receipt = await service.SubmitAsync(new(f.Session, null), body, static () => true, Token,
            beforeRetain: _ => { validations++; return System.Threading.Tasks.Task.CompletedTask; }, origin: origin);
        f.RetainCalls.Should().Be(1);
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        f.Adapter.Calls.Should().Be(1);
        tools.Should().Be(1);
        validations.Should().BeGreaterThan(4, "source validation surrounds execution and the actual host tool callback");
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be("The admitted host reported 1.0.");
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        receipt.Task.Revision.Value.Should().Be(3);
        receipt.Message.AssistantResponse.Provenance.Should().Be(f.Adapter.Provenance);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains(body, StringComparison.Ordinal)
            || message.Contains("host reported", StringComparison.Ordinal));
        using var inspection = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        (await service.ReadPageAsync(f.Session.SessionId, null, 25, Token)).Messages.Single().Should().Be(receipt.Message);
        var details = await service.ReadDetailAsync(new(f.Session, null), receipt.Message, static () => true, Token);
        details.Source.Should().Be(body);
        details.ConversationMessage!.AssistantResponse.Should().Be(receipt.Message.AssistantResponse);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("action")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("blank")]
    [InlineData("oversize")]
    [InlineData("control")]
    [InlineData("surrogate")]
    [InlineData("credential")]
    public async Task HostileProviderReplyNeverRetainsAnAssistantAnswerOrReportsSuccess(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        f.Adapter.Reply = fault switch
        {
            "provider" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.CopilotCandidate, new("foreign", null)),
            "action" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("not an effect", BuiltInAction.LockMachine)),
            "question" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new("not a final answer", null, Question: new("Choose", ["A", "B"]))),
            "grant" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new("not a grant", null, new(GrantChangeOperation.Add, BuiltInAction.ShowVersion, ModelApprovalScope.Session))),
            "blank" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(" ", null)),
            "oversize" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(new string('x', 4096), null)),
            "control" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("text\0", null)),
            "surrogate" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("\ud800", null)),
            _ => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("api_key=synthetic-only-value", null)),
        };
        var receipt = await f.ConversationService(host, execution: host).SubmitAsync(new(f.Session, null),
            "original request", static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Message.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("before-run")]
    [InlineData("before-tool")]
    [InlineData("after-tool")]
    [InlineData("metadata")]
    [InlineData("terminal")]
    [InlineData("audit")]
    [InlineData("readback")]
    public async Task SourceAndReceiptFailuresCannotPublishAnAnswerOrAutomaticallyRepeatExecution(string boundary)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var live = true;
        var toolCalls = 0;
        if (boundary is "before-run") { f.AfterExecutionStart = () => live = false; }
        f.OnVersion = () =>
        {
            toolCalls++;
            if (boundary is "after-tool") { live = false; }
        };
        f.Adapter.OnTool = async invoke =>
        {
            if (boundary is "before-tool") { live = false; }
            if (boundary is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
            await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token);
            if (boundary is "terminal") { live = false; }
        };
        if (boundary is "audit") { f.BeforeExecutionComplete = () => throw new IOException("required audit failed"); }
        if (boundary is "readback") { f.BadExecutionReadback = true; }
        await f.ConversationService(host, execution: host).Invoking(service =>
            service.SubmitAsync(new(f.Session, null), "immutable source", () => live, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Adapter.Calls.Should().Be(boundary is "before-run" ? 0 : 1);
        toolCalls.Should().Be(boundary is "before-run" or "before-tool" or "metadata" ? 0 : 1);
        f.RetainCalls.Should().Be(1);
        f.ExecutionStarts.Should().Be(1);
    }

    [Fact]
    public async Task HostDeadlineProducesTruthfulUnknownTaskWithoutAnAnswerOrRetry()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        f.Adapter.OnRun = f.Time.FireDeadline;
        var receipt = await f.ConversationService(host, execution: host).SubmitAsync(new(f.Session, null),
            "original timed request", static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
    }

    [Fact]
    public async Task CancelledNoncooperativeTurnHoldsOriginalUnknownWorkAndSlotUntilActualTerminationWithoutLateAnswer()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        using var cancellation = new CancellationTokenSource();
        var submission = f.ConversationService(host, execution: host).SubmitAsync(new(f.Session, null),
            "original request", static () => true, cancellation.Token);
        f.Adapter.Calls.Should().Be(1);
        cancellation.Cancel();
        Func<Task> observe = async () => { await submission; };
        await observe.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        host.IsQuiescent.Should().BeFalse();
        f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        f.Retained!.AssistantResponse.Should().BeNull();
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late forbidden answer", null)));
        await host.DisposeAsync();
        f.Retained.AssistantResponse.Should().BeNull();
        f.ExecutionCompletions.Should().Be(0);
    }

    private sealed partial class Fixture
    {
        internal int ExecutionStarts { get; private set; }
        internal int ExecutionCompletions { get; private set; }
        internal Action? AfterExecutionStart { get; set; }
        internal Action? BeforeExecutionComplete { get; set; }
        internal bool BadExecutionReadback { get; set; }
        internal string? StartReceiptFault { get; set; }
        internal string? TerminalReceiptFault { get; set; }

        public ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
            ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token)
        {
            var retained = ReadPendingRecord(request);
            admitted().Should().BeTrue();
            ExecutionStarts++;
            var message = retained.Message with { Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = metadataRevision };
            var receipt = new ConversationSubmissionReceipt(message, retained.Task.Next(HostTaskState.DispatchRecorded));
            RememberPendingRecord(receipt);
            AfterExecutionStart?.Invoke();
            return ValueTask.FromResult(StartReceiptFault switch
            {
                "body" => receipt with { Message = receipt.Message with { Body = "different receipt" } },
                "generation" => receipt with { Message = receipt.Message with { Generation = new(2) } },
                "sequence" => receipt with { Message = receipt.Message with { Sequence = 2 } },
                "task-request" => receipt with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(2), HostTaskState.DispatchRecorded) },
                "task-state" => receipt with { Task = new(request, new(2), HostTaskState.IntentRecorded) },
                "task-revision" => receipt with { Task = new(request, new(3), HostTaskState.DispatchRecorded) },
                _ => receipt,
            });
        }

        public ValueTask<ConversationSubmissionReceipt> CompleteConversationExecutionAsync(HostRequest request,
            ModelTurnResult result, Func<bool> admitted, CancellationToken token)
        {
            var retained = ReadPendingRecord(request);
            BeforeExecutionComplete?.Invoke();
            admitted().Should().BeTrue();
            ExecutionCompletions++;
            var message = retained.Message with
            {
                Revision = 3, Outcome = result.Outcome, Reason = result.Reason,
                AssistantResponse = result.Outcome == ModelTurnOutcome.Succeeded
                    ? ConversationResponse.FromResult(result, retained.Message.Execution!) : null,
                Observations = result.Observations,
                SubmittedContext = result.SubmittedContext,
            };
            var receipt = new ConversationSubmissionReceipt(message, retained.Task.Next(message.ExpectedTaskState));
            RememberPendingRecord(receipt);
            if (BadExecutionReadback) { Retained = Retained with { Body = "different durable original" }; }
            return ValueTask.FromResult(TerminalReceiptFault switch
            {
                "outcome" => receipt with { Message = receipt.Message with { Outcome = ModelTurnOutcome.Unknown } },
                "reason" => receipt with { Message = receipt.Message with { Reason = ModelTurnReason.HostInterrupted } },
                "answer" => receipt with { Message = receipt.Message with { AssistantResponse = null } },
                "observations" => receipt with { Message = receipt.Message with { Observations = null } },
                "submitted-context" => receipt with { Message = receipt.Message with { SubmittedContext = null } },
                "execution" => receipt with { Message = receipt.Message with { Execution = result.Provenance! with { Turn = new(Guid.NewGuid()) } } },
                "generation" => receipt with { Message = receipt.Message with { Generation = new(2) } },
                "sequence" => receipt with { Message = receipt.Message with { Sequence = 2 } },
                "revision" => receipt with { Message = receipt.Message with { Revision = 2 } },
                "task-revision" => receipt with { Task = new(request, new(4), HostTaskState.Succeeded) },
                _ => receipt,
            });
        }
    }
}
