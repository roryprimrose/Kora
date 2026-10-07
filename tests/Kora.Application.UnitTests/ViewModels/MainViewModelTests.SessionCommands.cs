using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Busy_bootstrap_keeps_exact_session_controls_available_without_cancelling_or_reasoning()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        BindSessions(fixture);
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = fixture.ViewModel.DetectMicrophonesAsync();
        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.CommandText = "session list";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await fixture.RunAsync("session list");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.CommandText = "session delete selected";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await fixture.RunAsync("session delete selected");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not accepted.");
        fixture.ViewModel.CommandText = "infer something";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Probe.Gate.SetResult();
        await busy;
    }

    [Fact]
    public async Task Session_typed_and_activated_voice_entry_points_share_exact_service_and_never_reason()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindSessions(fixture);
        await fixture.RunAsync("session create \"Label\"");
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"committed\"").And.Contain("\"name\":\"Label\"");
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RaiseActivatedTranscriptAsync("Kora, session status " + id, 1);
        fixture.ViewModel.ResponseBody.Should().Contain(id);
        store.Origins.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        await fixture.RaiseActivatedTranscriptAsync("Kora, session rename " + id + " 1 1 \"Voice label\"", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("Voice label");
        await fixture.RaiseActivatedTranscriptAsync("Kora, session done " + id + " 1", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("\"active\":false");
        await fixture.RaiseActivatedTranscriptAsync("Kora, session resume " + id + " 2", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("\"active\":true");
        await fixture.RunAsync("session help");
        fixture.ViewModel.ResponseBody.Should().Contain("session inspect");
        fixture.HostStore.Records.Select(record => record.Request.RequestId).Distinct().Should().HaveCount(6);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Session_invalid_and_unbound_commands_are_explicit_without_model_fallback()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("session delete named");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not accepted.");
        await fixture.RunAsync("session list");
        fixture.ViewModel.ResponseTitle.Should().Be("Session commands unavailable.");
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_commands_preserve_pending_question_grant_and_action_approval_targets()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        BindSessions(fixture);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.RunAsync("session list");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Session command blocked.");
        await fixture.ViewModel.CancelModelQuestionAsync();
        fixture.ViewModel.PrepareGrantChange(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await fixture.RunAsync("session help");
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.Reasoner.Question = null;
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.RunAsync("please lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.RunAsync("session help");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("create \"Label\"")]
    [InlineData("rename 12345678-1234-1234-1234-123456789012 1 0 \"Label\"")]
    [InlineData("done 12345678-1234-1234-1234-123456789012 1")]
    [InlineData("resume 12345678-1234-1234-1234-123456789012 1")]
    public async Task Protected_call_voice_mutations_are_explicitly_unavailable_not_queued(string arguments)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        BindSessions(fixture);
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, session " + arguments, 1);
        fixture.ViewModel.ResponseTitle.Should().Contain("unavailable during a protected call");
        fixture.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_private_reads_remain_available_during_protected_call_and_revision_race_fails_closed()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        BindSessions(fixture);
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, session list", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
        fixture.HostStore.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.IntentRecorded) { fixture.CallState.SetState(CallState.Clear); }
        };
        await fixture.RunAsync("session list");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"not-confirmed\"").And.Contain("no rollback");
        fixture.HostStore.Records.Last().State.Should().Be(HostTaskState.Denied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_task_typed_and_activated_voice_reads_and_protected_call_cancellation_share_host_workflow(bool closeAfterCommit)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindSessions(fixture);
        var request = new HostRequest(new(Guid.NewGuid()), store.Session.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var question = new HostQuestionRecord(new(request, new(Guid.NewGuid()), new(1)),
            LocalVersionWait.CreateSpec(), new(1), DateTimeOffset.UtcNow.AddMinutes(1));
        store.TaskResult = new(new(request, new(1), HostTaskState.IntentRecorded), new(1),
            LocalVersionWait.Source, true, question);
        var ids = request.SessionId.Value.ToString("D") + " " + request.TaskId.Value.ToString("D");
        await fixture.RunAsync("task status " + ids);
        fixture.ViewModel.ResponseBody.Should().Contain(request.TaskId.Value.ToString("D"))
            .And.Contain(LocalVersionWait.Source).And.Contain(question.Key.QuestionId.Value.ToString("D"));
        await fixture.RaiseActivatedTranscriptAsync("Kora, task inspect " + ids, 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        if (closeAfterCommit) { store.AfterCancellation = fixture.ViewModel.Dispose; }
        await fixture.RaiseActivatedTranscriptAsync("Kora, task cancel " + ids + " 1 1 "
            + question.Key.QuestionId.Value.ToString("D") + " 1", 1);
        if (!closeAfterCommit) { fixture.ViewModel.ResponseTitle.Should().Be("Session command committed."); }
        store.TaskResult.Task.State.Should().Be(HostTaskState.Cancelled);
        store.Origins.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("task delete named", "Session command not accepted.")]
    [InlineData("task help", "Session commands unavailable.")]
    public async Task Task_namespace_never_falls_back_to_model_or_unbound_authority(string text, string title)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("late-disposed")]
    [InlineData("late-locked")]
    public async Task Disposed_task_entry_or_late_storage_failure_never_publishes_private_content(string scenario)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindSessions(fixture);
        if (scenario is "late-disposed") { store.BeforeTaskRead = fixture.ViewModel.Dispose; }
        else if (scenario is "late-locked") { store.BeforeTaskRead = () => fixture.Session.IsUnlocked = false; }
        else { fixture.ViewModel.Dispose(); }
        await fixture.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.TaskInspect, store.Session.Authority.SessionId.Value)
            { TaskId = Guid.NewGuid() }, Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseBody.Should().NotContain("No task with that exact ID");
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (scenario is "disposed") { fixture.HostStore.Records.Should().BeEmpty(); }
    }

    private static SessionCommandStore BindSessions(Fixture fixture)
    {
        var store = new SessionCommandStore(fixture);
        fixture.ViewModel.BindSessionCommands(new(store, new(fixture.HostStore), store,
            NullLogger<SessionWorkspaceService>.Instance));
        return store;
    }

    [Fact]
    public async Task Voice_origin_without_enabled_trusted_channel_is_denied_before_host_intent()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        BindSessions(fixture);
        await fixture.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.List),
            Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command denied.");
        fixture.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Model_or_system_origin_cannot_acquire_session_management_authority()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        BindSessions(fixture);
        await fixture.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.List),
            Kora.Core.Auditing.SecurityAuditInitiator.ModelSuggestion);
        fixture.ViewModel.ResponseBody.Should().Contain("original trusted");
        fixture.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("unexpected")]
    public async Task Session_entry_reports_expected_failures_truthfully_and_does_not_hide_programming_failures(string kind)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindSessions(fixture);
        store.Failure = kind switch
        {
            "storage" => new IOException("Storage or required receipt failed"),
            "access" => new UnauthorizedAccessException("Private store access denied"),
            "cancel" => new OperationCanceledException("Observation cancelled"),
            _ => new NotSupportedException("Unexpected adapter error"),
        };
        if (kind is "unexpected")
        {
            var act = () => fixture.RunAsync("session list");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
        else
        {
            await fixture.RunAsync("session list");
            fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
            fixture.ViewModel.ResponseBody.Should().Contain("no rollback").And.Contain("exact ID and revisions");
        }
        fixture.HostStore.Records.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }

    private sealed class SessionCommandStore(Fixture fixture) : ISessionWorkspaceStore, ISessionWorkspaceAccess
    {
        internal HostTaskObservation? TaskResult { get; set; }
        internal Action? BeforeTaskRead { get; set; }
        internal Action? AfterCancellation { get; set; }
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken cancellationToken)
        {
            BeforeTaskRead?.Invoke();
            return ValueTask.FromResult(TaskResult);
        }
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> canControl, CancellationToken cancellationToken)
        {
            canControl().Should().BeTrue();
            var current = TaskResult ?? throw new InvalidOperationException("Unknown target.");
            target.TaskId.Should().Be(current.Task.Request.TaskId);
            target.QuestionRevision.Should().Be(current.Question!.Key.Revision);
            TaskResult = current with
            {
                Task = current.Task.Next(HostTaskState.Cancelled),
                Question = current.Question with { Key = current.Question.Key.Next(), Status = QuestionStatus.Cancelled },
            };
            AfterCancellation?.Invoke();
            return ValueTask.FromResult(TaskResult);
        }
        internal SessionWorkspaceEntry Session { get; private set; } =
            new(new(new(Guid.NewGuid()), new(1), true), null);
        internal List<RequestOrigin> Origins { get; } = [];
        internal Exception? Failure { get; set; }
        public bool CanInspect => fixture.ViewModel.CanRevealPrivatePresentation;
        public bool CanControl => CanInspect && !fixture.ViewModel.IsProtectedCall;
        public long ControlRevision => fixture.ViewModel.CallPolicyRevision;

        public async ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken)
        {
            Origins.Add(request.Origin);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await fixture.HostStore.CommitAsync(intent, 0, cancellationToken);
            return intent;
        }
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Session);
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
            Failure is null ? ValueTask.FromResult(new SessionPage<SessionWorkspaceEntry>([Session], null))
                : ValueTask.FromException<SessionPage<SessionWorkspaceEntry>>(Failure);
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> canControl, CancellationToken cancellationToken)
        {
            canControl().Should().BeTrue();
            Session = new(new(request.SessionId, new(1), true), new(request.SessionId, new(1), name));
            return ValueTask.FromResult(Session);
        }
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
            long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken)
        {
            canControl().Should().BeTrue();
            Session = Session with { Metadata = new(request.SessionId, new(expectedMetadataRevision + 1), name) };
            return ValueTask.FromResult(Session);
        }
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration,
            bool active, Func<bool> canControl, CancellationToken cancellationToken)
        {
            canControl().Should().BeTrue();
            Session = Session with { Authority = new(request.SessionId, new(expectedGeneration.Value + 1), active) };
            return ValueTask.FromResult(Session.Authority);
        }
    }
}
