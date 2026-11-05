using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InspectingNoRunningOwnerPreservesHeldPendingWorkAndExactCancellation(bool manualHold, bool stop)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var target = model.SelectedSessionRecord!;
                var active = await composed.Conversation.SubmitQueuedAsync(target, "active original", () => true, f.Token);
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                var queued = await composed.Conversation.SubmitQueuedAsync(target, "exact pending original", () => true, f.Token);
                await model.RefreshPendingConversationAsync();
                if (manualHold) { await model.HoldConversationDispatchAsync(); }
                if (stop)
                {
                    await model.RefreshRunningConversationAsync();
                    await model.CancelRunningConversationAsync();
                    active.Completion.IsCompleted.Should().BeFalse("Stop acceptance is not physical resource closure");
                }
                adapter.Release.SetResult(new(stop ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable,
                    provenance.Selection, stop ? new("forbidden late answer", null) : null));
                var terminal = (await active.Completion).RequireReceipt();
                terminal.Task.State.Should().Be(stop ? HostTaskState.Unknown : HostTaskState.Unavailable);
                terminal.Message.AssistantResponse.Should().BeNull("a non-cooperative provider's late success is not a retained answer");
                await model.RefreshPendingConversationAsync();
                var pending = model.PendingConversationTurns.Should().ContainSingle().Subject;
                pending.Submission.Message.Request.Should().Be(queued.Receipt.Message.Request);
                model.SelectedPendingConversation = pending;
                model.ComposerBody = "independent unsent next input";
                var retention = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
                var audits = composed.Audit.Events.Count;

                await model.RefreshRunningConversationAsync();

                model.RunningConversationStatus.Should().Contain("No active admitted conversation lease");
                model.PendingConversationTurns.Should().ContainSingle().Which.Should().BeSameAs(pending);
                model.SelectedPendingConversation.Should().BeSameAs(pending);
                model.CanCancelPendingConversation.Should().BeTrue();
                model.CanResumePendingConversation.Should().BeTrue();
                model.CanReleaseConversationDispatch.Should().Be(manualHold);
                model.ComposerBody.Should().Be("independent unsent next input");
                composed.Audit.Events.Count.Should().Be(audits, "running inspection is passive");
                (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(retention);
                queued.Completion.IsCompleted.Should().BeFalse("inspection must neither dispatch nor cancel the held original");

                await model.CancelPendingConversationAsync();
                var cancelled = (await queued.Completion).RequireReceipt();
                cancelled.Task.State.Should().Be(HostTaskState.Cancelled);
                cancelled.Message.Body.Should().Be("exact pending original");
                cancelled.Message.Execution.Should().BeNull();
                cancelled.Message.AssistantResponse.Should().BeNull();
                model.ComposerBody.Should().Be("independent unsent next input");
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                model.Close();
            }
        });
    }

    [Theory]
    [InlineData("current")]
    [InlineData("newer")]
    [InlineData("switch")]
    [InlineData("aba")]
    [InlineData("revision")]
    [InlineData("close")]
    [InlineData("cancel")]
    public async Task FailedRunningInspectionCannotEraseNewerOrRetiredStopPresentation(string continuation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new RunningInspectionStore(f.Store);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter, conversationControlStore: reader);
            var model = composed.Model;
            Task<ConversationTurnCompletion>? completion = null;
            Task? inspection = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                var target = model.SelectedSessionRecord!;
                var active = await composed.Conversation.SubmitQueuedAsync(target, "original running request", () => true, f.Token);
                completion = active.Completion;
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                await model.RefreshRunningConversationAsync();
                model.CanCancelRunningConversation.Should().BeTrue();
                reader.HoldNextRead = true;
                inspection = model.RefreshRunningConversationAsync();
                await reader.Started.Task.WaitAsync(f.Token);

                if (continuation is "newer") { await model.RefreshRunningConversationAsync(); }
                else if (continuation is "switch" or "aba")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                    model.ComposerBody = "unrelated unsent input";
                    if (continuation is "aba")
                    {
                        await model.SelectAsync(target);
                        await model.RefreshRunningConversationAsync();
                    }
                }
                else if (continuation is "revision") { composed.Access.ControlRevision++; }
                else if (continuation is "close") { model.Close(); }
                else if (continuation is "cancel")
                {
                    await model.CancelRunningConversationAsync();
                    adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("late forbidden answer", null)));
                    var terminal = (await completion).RequireReceipt();
                    terminal.Task.State.Should().Be(HostTaskState.Unknown);
                    terminal.Message.AssistantResponse.Should().BeNull();
                    await model.RefreshRunningConversationAsync();
                    model.RunningConversationStatus.Should().Contain("No active admitted conversation lease");
                }
                var status = model.RunningConversationStatus;
                var canStop = model.CanCancelRunningConversation;
                var audits = composed.Audit.Events.Count;
                reader.Release.SetException(new IOException("Synthetic delayed running inspection failure"));
                await inspection;

                if (continuation is "current")
                {
                    model.RunningConversationStatus.Should().Contain("inspection unconfirmed");
                    model.CanCancelRunningConversation.Should().BeFalse();
                    await model.RefreshRunningConversationAsync();
                    model.CanCancelRunningConversation.Should().BeTrue("a deliberate fresh read recovers the exact Stop control");
                    await model.CancelRunningConversationAsync();
                    completion.IsCompleted.Should().BeFalse("recovered Stop still waits for physical closure");
                }
                else
                {
                    model.RunningConversationStatus.Should().Be(status);
                    model.CanCancelRunningConversation.Should().Be(canStop);
                    composed.Audit.Events.Count.Should().Be(audits, "obsolete read failure cannot create a cancellation");
                }
                if (continuation is "switch") { model.ComposerBody.Should().Be("unrelated unsent input"); }
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally
            {
                reader.Release.TrySetException(new IOException("Owned inspection cleanup"));
                if (inspection is not null) { await inspection; }
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                if (completion is not null)
                {
                    try { await completion; }
                    catch (ConversationSubmissionUnconfirmedException) when (continuation is "revision") { }
                }
                model.Close();
            }
        });
    }

    private sealed class RunningInspectionStore(ISessionWorkspaceStore inner) : ISessionWorkspaceStore
    {
        internal bool HoldNextRead { get; set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<SessionWorkspaceEntry> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            if (!HoldNextRead) { return inner.ReadMetadataAsync(session, token); }
            HoldNextRead = false;
            Started.SetResult();
            return new(Release.Task);
        }
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration,
            long expectedMetadataRevision, CancellationToken token) => inner.PreviewDispositionAsync(session, expectedGeneration, expectedMetadataRevision, token);
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> canControl, CancellationToken token) => inner.DisposeSessionAsync(request, preview, canControl, token);
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => inner.RecordControlIntentAsync(request, token);
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => inner.ReadSessionsAsync(after, limit, token);
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => inner.ReadMetadataPageAsync(after, limit, token);
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) => inner.ReadTaskAsync(session, task, token);
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> canControl, CancellationToken token) => inner.CancelWaitingTaskAsync(control, target, canControl, token);
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> canControl, CancellationToken token) => inner.CreateNamedSessionAsync(request, name, canControl, token);
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration, long expectedMetadataRevision,
            SessionName name, Func<bool> canControl, CancellationToken token) => inner.RenameSessionAsync(request, expectedGeneration, expectedMetadataRevision, name, canControl, token);
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
            inner.ReadQuestionPageAsync(session, after, limit, token);
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
            inner.ReadTaskPageAsync(session, after, limit, token);
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active,
            Func<bool> canControl, CancellationToken token) => inner.ChangeIdleLifecycleAsync(request, expectedGeneration, active, canControl, token);
    }
}
