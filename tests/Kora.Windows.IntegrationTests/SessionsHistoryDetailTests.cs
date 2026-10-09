using Avalonia.Automation.Peers;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsHistoryDetailTests
{
    [WindowsFact]
    public async Task Selected_receipt_details_preserve_work_question_focus_queue_and_retention_without_writes()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            AdmittedDetailContent? presented = null;
            var viewer = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, openHistoryDetail: content =>
                { presented = content; return "Resolved exact receipt."; });
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            viewer.SelectWorkRecord(viewer.WorkRecords.Single());
            var work = viewer.SelectedWorkRecord;
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var focus = window.FindControl<Button>("ReadHistory")!;
                focus.Focus().Should().BeTrue();
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var queue = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                var events = f.Count("host_task_events");
                var history = f.Count("session_history");
                viewer.HistorySessionId = f.Request.SessionId.Value.ToString("D").ToUpperInvariant();
                await viewer.ReadHistoryAsync();
                viewer.CanOpenHistoryDetail.Should().BeFalse();
                var receipt = viewer.HistoryRecords[0];
                viewer.SelectHistoryRecord(receipt);
                viewer.CanOpenHistoryDetail.Should().BeTrue();
                await viewer.OpenHistoryDetailAsync();
                presented!.HistorySession.Should().Be(f.Request.SessionId);
                presented.Reference.ItemId.Value.Should().Be(receipt.Id);
                presented.Reference.Revision.Should().Be(receipt.Sequence);
                presented.Source.Should().Contain(receipt.ProvenanceDigest);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(focus);
                viewer.SelectedWorkRecord.Should().BeSameAs(work);
                viewer.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Should().BeEquivalentTo(queue);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_task_events").Should().Be(events);
                f.Count("session_history").Should().Be(history);
                ControlAutomationPeer.CreatePeerForElement(window.FindControl<Button>("OpenHistoryDetail")!)!
                    .GetName().Should().Contain("read-only native details");
                viewer.HistorySessionId = Guid.NewGuid().ToString("D");
                viewer.HistoryRecords.Should().BeEmpty();
                viewer.CanOpenHistoryDetail.Should().BeFalse();
                access.CanInspect = false;
                await viewer.OpenHistoryDetailAsync();
                viewer.Detail.Should().BeEmpty();
                viewer.Status.Should().Contain("unavailable");
            }
            finally { window.Close(); }
            viewer.HistoryRecords.Should().BeEmpty();
            viewer.SelectedHistoryRecord.Should().BeNull();
        });
    }

    [WindowsFact]
    public async Task Restart_and_disposition_resolve_only_retained_exact_metadata_never_old_private_content_or_work()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var page = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        var receipt = page.Records.Single(record => record.Kind == SessionHistoryKind.Answer);
        var original = await service.ReadHistoryDetailAsync(f.Request.SessionId, receipt.Id, f.Token);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var restored = await service.ReadHistoryDetailAsync(f.Request.SessionId, receipt.Id, f.Token);
        restored.IsSameSnapshot(original).Should().BeTrue();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        HostId<SessionIdentity>? revoked = null;
        var viewer = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance, revokeHistoryDetails: session =>
            {
                f.Count("host_questions").Should().Be(1, "private viewers retire before logical disposition");
                revoked = session;
            });
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await viewer.PreviewDispositionAsync();
        await viewer.ConfirmDispositionAsync();
        revoked.Should().Be(f.Request.SessionId);
        viewer.Close();
        var redacted = await service.ReadHistoryDetailAsync(f.Request.SessionId, receipt.Id, f.Token);
        redacted.Reference.Should().Be(original.Reference);
        redacted.IsSameSnapshot(original).Should().BeFalse();
        redacted.Source.Should().Contain("Redacted").And.NotContain(question.Spec.Text);
        var foreign = () => service.ReadHistoryDetailAsync(new(Guid.NewGuid()), receipt.Id, f.Token);
        await foreign.Should().ThrowAsync<Exception>();
        (await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token)).Records
            .Should().OnlyContain(record => record.Availability == SessionHistoryAvailability.Redacted);
    }
}
