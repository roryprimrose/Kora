using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsWorkWindowTests
{
    private static readonly string[] FixedProfileButtons = ["EnqueueCapabilities", "EnqueueReadiness", "EnqueueRuntimes"];
    private static readonly string[] RetiredSourceButtons = ["Done", "Resume", "SubmitConversation", "CancelQueueEntry"];
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Worker_thread_lifecycle_revocation_retires_matching_source_and_preserves_shared_window_and_unrelated_input(bool matching)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            const string targetBody = "  target input e\u0301\r\n  ";
            const string otherBody = "  unrelated input \u03a9 e\u0301\r\nexact trailing space  ";
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            using var controller = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance);
            SessionsWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((window, _) => opened = window);
            controller.Bind();
            controller.Open();
            opened.Should().NotBeNull();
            var viewer = (SessionsViewModel)opened!.DataContext!;
            await viewer.RefreshAsync();
            var target = fixture.LifecycleTarget!;
            var targetRow = viewer.Sessions.Single(entry => entry.Authority.SessionId == target.SessionId);
            var other = viewer.Sessions.First(entry => entry.Authority.SessionId != target.SessionId);
            var targetDraft = await fixture.Interactions.ReadConversationDraftAsync(target.SessionId, fixture.Token);
            var otherDraft = await fixture.Interactions.ReadConversationDraftAsync(other.Authority.SessionId, fixture.Token);
            await viewer.SelectAsync(other);
            viewer.ComposerBody = otherBody;
            await viewer.SelectAsync(targetRow);
            viewer.ComposerBody = targetBody;
            var selected = matching ? targetRow : other;
            await viewer.SelectAsync(selected);
            viewer.ComposerBody.Should().Be(matching ? targetBody : otherBody);
            var selectedObservation = viewer.SelectedSessionRecord;
            var rows = viewer.WorkRecords.ToArray();
            var source = viewer.CaptureDraftPresentationEligibility();
            var originalWindow = opened;
            var changed = await Task.Run(fixture.AdvanceLifecycleTargetAsync);
            changed.Generation.Value.Should().Be(target.Generation.Value + 1);
            changed.IsActive.Should().BeFalse();
            opened.IsVisible.Should().BeTrue("exact-source retirement must not dispose the shared composer owner");
            opened.DataContext.Should().BeSameAs(viewer);
            viewer.CanRead.Should().BeTrue("fresh admitted inspection remains available after exact-source retirement");
            source().Should().Be(!matching);
            if (matching)
            {
                viewer.WorkRecords.Should().BeEmpty();
                viewer.ReferencesSession(target.SessionId).Should().BeFalse();
                viewer.SelectedSessionRecord.Should().BeNull();
                viewer.ComposerBody.Should().BeEmpty();
                viewer.ConversationMessages.Should().BeEmpty();
                viewer.CanCancelWork.Should().BeFalse();
                foreach (var name in RetiredSourceButtons)
                {
                    opened.FindControl<Button>(name)!.IsEnabled.Should().BeFalse();
                }
                opened.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeNullOrEmpty();
            }
            else
            {
                viewer.SelectedSessionRecord.Should().BeSameAs(selectedObservation);
                viewer.WorkRecords.Should().Equal(rows);
                viewer.ComposerBody.Should().Be(otherBody);
                viewer.Sessions.Should().BeEmpty("list metadata is revoked without retargeting unrelated work");
                viewer.Status.Should().Contain("Rows and search continuation revoked");
            }
            controller.Open();
            opened.Should().BeSameAs(originalWindow);
            opened.DataContext.Should().BeSameAs(viewer);
            viewer.SessionFilter = SessionListFilter.All;
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == other.Authority.SessionId));
            viewer.ComposerBody.Should().Be(otherBody);
            opened.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(otherBody);
            await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == target.SessionId));
            viewer.SelectedSessionRecord!.Authority.Should().Be(changed);
            viewer.ComposerBody.Should().BeEmpty("the retired target input cannot return after fresh inspection");
            (await fixture.Interactions.ReadConversationDraftAsync(target.SessionId, fixture.Token))
                .Should().Be(targetDraft with { Session = changed });
            (await fixture.Interactions.ReadConversationDraftAsync(other.Authority.SessionId, fixture.Token))
                .Should().Be(otherDraft);
            await viewer.SelectAsync(viewer.Sessions.Single(entry => entry.Authority.SessionId == other.Authority.SessionId));
            viewer.ComposerBody.Should().Be(otherBody);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            opened.Closed += (_, _) => closed.TrySetResult();
            controller.Dispose();
            await closed.Task.WaitAsync(fixture.Token);
            opened.IsVisible.Should().BeFalse("whole-owner disposal still closes the shared presentation");
            viewer.CanRead.Should().BeFalse();
            viewer.ComposerBody.Should().BeEmpty();
            viewer.ReferencesSession(other.Authority.SessionId).Should().BeFalse();
            await Task.Run(() => controller.RevokeSession(target.SessionId));
            opened.IsVisible.Should().BeFalse();
        });
    }

    [WindowsFact]
    public async Task Retention_invalidation_preserves_unrelated_work_and_closure_clears_matching_live_work()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(fixture);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var viewer = CreateViewer(fixture, access);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.SelectWorkRecord(viewer.WorkRecords.Single());
        var session = fixture.Request.SessionId;
        var other = new HostId<SessionIdentity>(Guid.NewGuid());
        viewer.ReferencesSession(session).Should().BeTrue();
        viewer.ReferencesSession(other).Should().BeFalse();
        var records = viewer.WorkRecords;
        viewer.RevokeSessionList();
        viewer.Sessions.Should().BeEmpty();
        viewer.WorkRecords.Should().BeSameAs(records);
        viewer.SelectedSessionRecord!.Authority.SessionId.Should().Be(session);
        viewer.ReferencesSession(session).Should().BeTrue();
        viewer.Close();
        viewer.WorkRecords.Should().BeEmpty();
        viewer.PendingQuestions.Should().BeEmpty();
        viewer.CanCancelWork.Should().BeFalse();
        viewer.CanRefreshWork.Should().BeFalse();
        viewer.ReferencesSession(session).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Refresh_preserves_keyboard_focus_exact_selection_and_pending_question_coexistence_without_activity()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(fixture);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var viewer = CreateViewer(fixture, access);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var row = viewer.WorkRecords.Single();
            viewer.SelectWorkRecord(row);
            viewer.CanCancelWork.Should().BeTrue();
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var close = window.FindControl<Button>("ReadQueue")!;
                close.Focus().Should().BeTrue();
                var audits = fixture.Count("security_audit_events");
                var selected = viewer.SelectedSessionRecord!.Authority.SessionId;
                await viewer.RefreshWorkAsync();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(close);
                viewer.SelectedSessionRecord!.Authority.SessionId.Should().Be(selected);
                viewer.SelectedWorkRecord!.TaskId.Should().Be(row.TaskId);
                viewer.CanCancelWork.Should().BeFalse("a passive update never renews an inspected cancellation target");
                viewer.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                viewer.HistorySessionId = selected.Value.ToString("D");
                await viewer.ReadHistoryAsync();
                viewer.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                viewer.SelectedWorkRecord!.TaskId.Should().Be(row.TaskId);
                fixture.Count("security_audit_events").Should().Be(audits);
                var work = window.FindControl<ListBox>("WorkRecords")!;
                ControlAutomationPeer.CreatePeerForElement(work)!.GetName().Should().Contain("selection is passive");
                ControlAutomationPeer.CreatePeerForElement(window.FindControl<Button>("RemoveQueueEntry")!)!
                    .GetName().Should().Contain("exact selected pending queue ID");
                foreach (var name in FixedProfileButtons)
                {
                    window.FindControl<Button>(name).Should().BeNull();
                }
                fixture.Time.Now = question.ExpiresAt;
                await viewer.RefreshWorkAsync();
                viewer.WorkRecords.Single().State.Should().Contain("User-wait expired");
                viewer.SelectWorkRecord(viewer.WorkRecords.Single());
                viewer.CanCancelWork.Should().BeFalse();
                fixture.Count("security_audit_events").Should().Be(audits);
                viewer.SessionFilter = SessionListFilter.Done;
                viewer.WorkRecords.Should().BeEmpty();
                fixture.Count("security_audit_events").Should().Be(audits);
            }
            finally { window.Close(); }
            viewer.WorkRecords.Should().BeEmpty();
            viewer.PendingQuestions.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task Passive_refresh_keeps_the_focused_work_container_and_updates_its_observation_without_retargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(fixture);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var viewer = CreateViewer(fixture, access);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var row = viewer.WorkRecords.Single();
            viewer.SelectWorkRecord(row);
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var records = window.FindControl<ListBox>("WorkRecords")!;
                records.UpdateLayout();
                var container = records.ContainerFromItem(row)
                    ?? throw new InvalidOperationException("The displayed work row did not realize its native container.");
                container.Focus().Should().BeTrue();
                var changed = new List<string?>();
                row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
                fixture.Time.Now = fixture.Time.Now.AddSeconds(1);
                await viewer.RefreshWorkAsync();
                changed.Should().BeEmpty("a new observation time alone must not reannounce unchanged work fields");
                row.ToString().Should().Contain(row.TaskId.ToString("D")).And.Contain("Waiting for user");
                fixture.Time.Now = question.ExpiresAt;
                await viewer.RefreshWorkAsync();
                viewer.WorkRecords.Should().ContainSingle().Which.Should().BeSameAs(row);
                viewer.SelectedWorkRecord.Should().BeSameAs(row);
                records.ContainerFromItem(row).Should().BeSameAs(container);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(container);
                row.ObservedAt.Should().Be(fixture.Time.Now);
                row.State.Should().Contain("User-wait expired");
                changed.Should().Contain(nameof(SessionWorkRow.State));
                viewer.CanCancelWork.Should().BeFalse("stable presentation identity does not renew inspected authority");
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task Concurrent_queue_change_rejects_stale_native_controls_then_refresh_exposes_truthful_terminal_records()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access,
            new NoExecution(), new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var viewer = CreateViewer(fixture, access, service);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, fixture.Request.SessionId.Value, 1)
        { WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() }, RequestOrigin.LocalUi, () => true, fixture.Token);
        await viewer.RefreshWorkAsync();
        var row = viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending);
        viewer.SelectWorkRecord(row);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var prior = (await service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token)).Queue;
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, fixture.Request.SessionId.Value, 1)
        {
            QueueRevision = prior.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(),
        }, RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        await viewer.RemoveQueueEntryAsync();
        viewer.Status.Should().Contain("revision conflicts");
        (await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token)).Entries.Should().HaveCount(2);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.SelectWorkRecord(viewer.WorkRecords.Single(record => record.TaskId == row.TaskId));
        await viewer.RemoveQueueEntryAsync();
        viewer.WorkRecords.Should().Contain(record => record.TaskId == row.TaskId && record.Queue!.Entry.State == SessionQueueState.Removed);
        viewer.PendingQuestions.Should().BeEmpty();
        viewer.Close();
        await viewer.RefreshWorkAsync();
        (await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token)).Entries.Should().ContainSingle();
    }

    [WindowsFact]
    public async Task Disposed_or_unknown_subjects_clear_live_content_and_preserve_exact_redacted_history_recovery()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var viewer = CreateViewer(fixture, access, service);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await service.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 0, fixture.Token);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await viewer.RefreshWorkAsync();
        viewer.WorkRecords.Should().BeEmpty();
        viewer.CanCancelWork.Should().BeFalse();
        viewer.Status.Should().Contain("Disposed");
        viewer.HistorySessionId = fixture.Request.SessionId.Value.ToString("D");
        await viewer.ReadHistoryAsync();
        viewer.Status.Should().Contain("Disposed: content redacted");
        viewer.Close();
    }

    private static SessionsViewModel CreateViewer(InteractionStorageFixture fixture,
        WindowsSqliteSessionWorkspaceTests.Access access, SessionWorkspaceService? service = null)
    {
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        return new(service ?? WindowsSqliteSessionWorkspaceTests.Service(fixture, access),
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
    }

    [WindowsFact]
    public async Task Session_selection_change_during_a_native_control_invalidates_its_captured_target_without_execution()
    {
        using var fixture = new InteractionStorageFixture();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access,
            new NoExecution(), new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var other = await service.CreateAsync(new("Other exact subject"), RequestOrigin.LocalUi, fixture.Token);
        var viewer = CreateViewer(fixture, access, service);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single(record => record.Authority.SessionId == fixture.Request.SessionId));
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, fixture.Request.SessionId.Value, 1)
        { WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() }, RequestOrigin.LocalUi, () => true, fixture.Token);
        await viewer.RefreshWorkAsync();
        viewer.SelectWorkRecord(viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending));
        checkpoint.Commit = (_, _) => viewer.SelectAsync(other).IsCompletedSuccessfully.Should().BeTrue();
        try { await viewer.CancelWorkAsync(); }
        finally { checkpoint.Commit = null; viewer.Close(); }
        (await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token)).Entries.Should().ContainSingle()
            .Which.State.Should().Be(SessionQueueState.Pending);
        (await fixture.Store.ReadQueueAsync(other.Authority.SessionId, fixture.Token)).Entries.Should().BeEmpty();
    }

    private sealed class NoExecution : IDeterministicVersionQueueAction
    {
        public Kora.Core.Tools.CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null) =>
            throw new InvalidOperationException("Browsing and stale controls must never execute.");
    }
}
