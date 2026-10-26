using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task ExactRunningCancellationJoinsNoncooperativeAdapterBeforeTerminalAndNeverRetainsLateSuccessOrFault(
        bool lateFault, RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var statuses = new List<ConversationExecutionStatus>();
        ActivityContext originalContext = default;
        service.RunningConversationChanged += status =>
        {
            statuses.Add(status);
            if (status.Phase == ConversationExecutionPhase.Running) { originalContext = Activity.Current!.Context; }
        };
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original request", static () => true, Token, origin: origin);
        var running = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
        running.Phase.Should().Be(ConversationExecutionPhase.Running);
        running.Target.Request.Should().Be(f.Retained!.Request);
        var accepted = await service.CancelRunningConversationAsync(running.Target, static () => true, Token, origin);
        accepted.Phase.Should().Be(ConversationExecutionPhase.Cancelling);
        (await service.CancelRunningConversationAsync(running.Target, static () => true, Token, origin))
            .Phase.Should().Be(ConversationExecutionPhase.Cancelling);
        f.CancelAudit.Events.Should().HaveCount(2, "double cancellation is idempotent, not a new intent");
        f.CancelAudit.Events[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        f.CancelAudit.Events[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        f.CancelAudit.Activities.Should().OnlyContain(activity => activity.Links.Any(link => link.Context == originalContext)
            && activity.Status == ActivityStatusCode.Ok && activity.IsStopped);
        f.CancelAudit.Contexts.Should().OnlyContain(request => request.SessionId == target.Authority.SessionId
            && request.TaskId == running.Target.Request.TaskId && request.Origin == origin
            && request.RequestId != running.Target.Request.RequestId);
        submission.IsCompleted.Should().BeFalse("a token and logical host result do not prove adapter termination");
        f.ExecutionCompletions.Should().Be(0);
        host.IsQuiescent.Should().BeFalse();
        f.Retained.AssistantResponse.Should().BeNull();
        var disposal = host.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse("disposal must join the same actual owned adapter");
        if (lateFault) { pending.SetException(new IOException("late provider fault")); }
        else { pending.SetResult(LateAnswer()); }
        var receipt = await submission;
        await disposal;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.Reason.Should().Be(ModelTurnReason.CallerCancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown, "the dispatched task contract cannot claim rollback");
        f.ExecutionCompletions.Should().Be(1);
        f.Adapter.Calls.Should().Be(1);
        statuses.Last().Phase.Should().Be(ConversationExecutionPhase.Terminal);
        statuses.Last().Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        statuses.Last().TaskState.Should().Be(HostTaskState.Unknown);
        (await service.ReadRunningConversationAsync(target, static () => true, Token)).Should().BeNull();
        await service.Invoking(value => value.CancelRunningConversationAsync(running.Target, static () => true, Token, origin))
            .Should().ThrowAsync<InvalidOperationException>("a terminal/removed lease cannot be replayed");
        f.Messages.Should().NotContain(text => text.Contains("original request", StringComparison.Ordinal)
            || text.Contains("late answer", StringComparison.Ordinal) || text.Contains("late provider fault", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationBeforeProviderDispatchRetainsOneCancelledOriginalWithoutInvokingAdapter()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        Task<ConversationExecutionStatus>? cancel = null;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Running)
            { cancel = service.CancelRunningConversationAsync(status.Target, static () => true, Token); }
        };
        var receipt = await service.SubmitAsync(new(f.Session, null), "cancel before send", static () => true, Token);
        await cancel!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Task.State.Should().Be(HostTaskState.Unknown, "durable dispatch already won before provider cancellation admission");
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("privacy")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("task-terminal")]
    [InlineData("current-state")]
    [InlineData("current-generation")]
    [InlineData("current-revision")]
    [InlineData("current-request")]
    [InlineData("inactive")]
    [InlineData("private-revision")]
    [InlineData("audit-request")]
    [InlineData("audit-outcome")]
    [InlineData("missing-audit")]
    [InlineData("expiry-during-read")]
    [InlineData("clock-back-during-read")]
    [InlineData("expiry-during-audit")]
    [InlineData("privacy-during-audit")]
    [InlineData("caller")]
    [InlineData("model-origin")]
    public async Task WrongForeignStaleOrUnauditedCancellationNeverSignalsRunningWork(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host, cancellationAuditAvailable: fault is not "missing-audit");
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var session = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(session, "original", static () => true, Token);
        var original = (await service.ReadRunningConversationAsync(session, static () => true, Token))!.Target;
        var target = fault switch
        {
            "lease" => original with { LeaseId = Guid.NewGuid() },
            "session" => original with { Request = new(original.Request.RequestId, new(Guid.NewGuid()), original.Request.TaskId, original.Request.Origin) },
            "task" => original with { Request = new(original.Request.RequestId, original.Request.SessionId, new(Guid.NewGuid()), original.Request.Origin) },
            "generation" => original with { Generation = new(2) },
            "revision" => original with { TaskRevision = new(1) },
            _ => original,
        };
        var priorTask = f.Task;
        var priorSession = f.Session;
        var priorControl = f.ControlRevision;
        if (fault is "expired") { f.Time.Now = original.ExpiresAt; }
        if (fault is "clock-back") { f.Time.Now = original.CreatedAt.AddTicks(-1); }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "source") { f.Task = f.Task! with { CurrentSource = false }; }
        if (fault is "task-terminal") { f.Task = f.Task! with { Task = f.Task.Task.Next(HostTaskState.Succeeded) }; }
        if (fault is "current-state") { f.Task = f.Task! with { Task = new(f.Task.Task.Request, f.Task.Task.Revision, HostTaskState.Succeeded) }; }
        if (fault is "current-generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "current-revision") { f.Task = f.Task! with { Task = new(original.Request, new(3), HostTaskState.DispatchRecorded) }; }
        if (fault is "current-request") { f.Task = f.Task! with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(2), HostTaskState.DispatchRecorded) }; }
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "private-revision") { f.ControlRevision++; }
        if (fault is "audit-request") { f.CancelAudit.FailAt = 0; }
        if (fault is "audit-outcome") { f.CancelAudit.FailAt = 1; }
        if (fault is "expiry-during-read") { f.OnRead = () => f.Time.Now = original.ExpiresAt; }
        if (fault is "clock-back-during-read") { f.OnRead = () => f.Time.Now = original.CreatedAt.AddTicks(-1); }
        f.CancelAudit.OnWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            if (fault is "expiry-during-audit") { f.Time.Now = original.ExpiresAt; }
            if (fault is "privacy-during-audit") { f.ControlRevision++; }
        };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "caller") { caller.Cancel(); }
        await service.Invoking(value => value.CancelRunningConversationAsync(target, () => fault is not "selection",
            caller.Token, fault is "model-origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi))
            .Should().ThrowAsync<Exception>();
        f.CanControl = true;
        f.OnRead = null;
        f.Time.Now = original.CreatedAt;
        f.Task = priorTask;
        f.Session = priorSession;
        f.ControlRevision = priorControl;
        (await service.ReadRunningConversationAsync(session, static () => true, Token))!.Phase
            .Should().Be(ConversationExecutionPhase.Running);
        pending.SetResult(LateAnswer());
        (await submission).Task.State.Should().Be(HostTaskState.Succeeded);
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AcceptedCancellationAfterGenuineProviderSuccessButBeforeCommitSuppressesThatAnswer()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        Kora.Application.Hosting.SessionConversationService? service = null;
        ConversationCancellationTarget? target = null;
        var execution = new CancellationBoundaryExecution(host, async () =>
        {
            await service!.CancelRunningConversationAsync(target!, static () => true, Token);
        });
        service = f.ConversationService(host, execution: execution);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Running) { target = status.Target; }
        };
        var receipt = await service.SubmitAsync(new(f.Session, null), "original", static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        f.Audits.Should().Contain(audit => audit.Outcome == SecurityAuditOutcome.Succeeded,
            "the actual provider result succeeded, but cancellation won before answer retention");
        f.ExecutionCompletions.Should().Be(1);
    }

    [Fact]
    public async Task LogicalDeadlineCannotPublishQuiescentTerminalUntilNoncooperativeProviderActuallyJoins()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        ConversationExecutionStatus? last = null;
        service.RunningConversationChanged += status => last = status;
        var submission = service.SubmitAsync(new(f.Session, null), "original", static () => true, Token);
        f.Time.FireDeadline();
        submission.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        pending.SetResult(LateAnswer());
        var receipt = await submission;
        host.IsQuiescent.Should().BeTrue();
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        receipt.Message.AssistantResponse.Should().BeNull();
        last!.Phase.Should().Be(ConversationExecutionPhase.Terminal);
        last.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
    }

    private sealed class CancellationBoundaryExecution(IModelTurnExecution host, Func<Task> beforeReturn) : IModelTurnExecution
    {
        public async Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null)
        {
            var result = await host.RunAsync(turn, admitted, token, validateSource);
            result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            await beforeReturn();
            return result;
        }

        public Task WaitForQuiescenceAsync(CancellationToken cancellationToken) => host.WaitForQuiescenceAsync(cancellationToken);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("storage")]
    [InlineData("readback")]
    [InlineData("terminal-audit")]
    public async Task AcceptedCancellationStillJoinsWhenPrivateAdmissionOrTerminalRetentionBecomesUncertain(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        ConversationExecutionStatus? last = null;
        service.RunningConversationChanged += status => last = status;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original", static () => true, Token);
        var observed = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
        if (fault is "terminal-audit")
        {
            f.OnAudit = audit =>
            {
                if (audit.Outcome == SecurityAuditOutcome.Cancelled) { throw new IOException("required terminal audit unavailable"); }
            };
        }
        await service.CancelRunningConversationAsync(observed.Target, static () => true, Token);
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "storage") { f.BeforeExecutionComplete = () => throw new IOException("uncertain terminal storage"); }
        if (fault is "readback") { f.BadExecutionReadback = true; }
        submission.IsCompleted.Should().BeFalse();
        pending.SetResult(LateAnswer());
        Func<Task> observe = async () => { await submission; };
        await observe.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        host.IsQuiescent.Should().BeTrue();
        last!.Phase.Should().Be(ConversationExecutionPhase.Unconfirmed);
        f.Retained!.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalCommitBoundaryWinsTruthfullyWithoutRetroactiveCancellationOrAnswerRollback(bool uncertainCommit)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        ConversationCancellationTarget? target = null;
        Task<ConversationExecutionStatus>? attempted = null;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Running) { target = status.Target; }
        };
        f.BeforeExecutionComplete = () =>
        {
            attempted = service.CancelRunningConversationAsync(target!, static () => true, Token);
        };
        if (uncertainCommit) { f.BadExecutionReadback = true; }
        var submission = service.SubmitAsync(new(f.Session, null), "original", static () => true, Token);
        if (uncertainCommit)
        {
            Func<Task> observe = async () => { await submission; };
            await observe.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        }
        else { (await submission).Task.State.Should().Be(HostTaskState.Succeeded); }
        Func<Task> observeCancellation = async () => { await attempted!; };
        await observeCancellation.Should().ThrowAsync<InvalidOperationException>();
        f.Retained!.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Retained.AssistantResponse!.Body.Should().Be("answer");
        f.CancelAudit.Events.Should().BeEmpty("the completion gate rejected cancellation before acceptance audit");
    }

    private static Kora.Application.Dependencies.ModelTurnHost ConfigureCancellation(Fixture f)
    {
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        return f.ConfiguredProfiles();
    }

    private static ModelProviderReply LateAnswer() =>
        new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null));

    private sealed partial class Fixture
    {
        internal CancellationAudit CancelAudit { get; } = new();
    }

    private sealed class CancellationAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Contexts { get; } = [];
        internal List<Activity> Activities { get; } = [];
        internal int? FailAt { get; set; }
        internal Action<SecurityAuditEvent>? OnWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Events.Count == FailAt) { throw new IOException("required cancellation audit unavailable"); }
            Contexts.Add(HostActivity.RequireCurrent().Request);
            Activities.Add(Activity.Current!);
            Events.Add(auditEvent);
            OnWrite?.Invoke(auditEvent);
        }
    }
}