using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsWorkWindowTests
{
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
        viewer.CanEnqueueVersion.Should().BeFalse();
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
        await viewer.EnqueueVersionAsync();
        var row = viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending);
        viewer.SelectWorkRecord(row);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var prior = (await service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token)).Queue;
        await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, fixture.Request.SessionId.Value, 1)
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
        await viewer.DispatchQueueAsync();
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
        await viewer.EnqueueVersionAsync();
        viewer.SelectWorkRecord(viewer.WorkRecords.Single(record => record.Queue?.Entry.State == SessionQueueState.Pending));
        checkpoint.Commit = (_, _) => viewer.SelectAsync(other).GetAwaiter().GetResult();
        try { await viewer.CancelWorkAsync(); }
        finally { checkpoint.Commit = null; viewer.Close(); }
        (await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token)).Entries.Should().ContainSingle()
            .Which.State.Should().Be(SessionQueueState.Pending);
        (await fixture.Store.ReadQueueAsync(other.Authority.SessionId, fixture.Token)).Entries.Should().BeEmpty();
    }

    private sealed class NoExecution : IDeterministicVersionQueueAction
    {
        public Kora.Core.Tools.CapabilityReply Observe(CancellationToken token) =>
            throw new InvalidOperationException("Browsing and stale controls must never execute.");
    }
}
