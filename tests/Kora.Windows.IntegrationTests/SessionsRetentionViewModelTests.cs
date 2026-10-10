using System.Globalization;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class SessionsRetentionViewModelTests
{
    [WindowsFact]
    public async Task Exact_native_status_and_two_step_due_ordinary_confirmation_never_renew_clock_or_cleanup()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        var clock = await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
        var audits = fixture.Count("security_audit_events");
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.RetentionStatus.Should().Contain(clock.LastMeaningfulActivity.ToString("O", CultureInfo.InvariantCulture))
            .And.Contain(clock.ArchiveDue.ToString("O", CultureInfo.InvariantCulture))
            .And.Contain(clock.DeleteDue.ToString("O", CultureInfo.InvariantCulture));
        fixture.Count("security_audit_events").Should().Be(audits);
        viewer.CanKeepSession.Should().BeTrue();
        viewer.PreviewRetention(keep: true);
        viewer.CanConfirmRetention.Should().BeTrue();
        viewer.CancelRetentionReview();
        viewer.CanConfirmRetention.Should().BeFalse();
        viewer.PreviewRetention(keep: true);
        await viewer.ConfirmRetentionAsync();
        viewer.Status.Should().Contain("readback committed");
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock with { Perpetual = true });
        fixture.Time.Now = clock.DeleteDue.AddDays(1);
        await viewer.RefreshRetentionAsync();
        viewer.PreviewRetention(keep: false);
        viewer.RetentionPreview.Should().Contain("already-due").And.Contain("background maintenance")
            .And.Contain(clock.DeleteDue.ToString("O", CultureInfo.InvariantCulture));
        viewer.CanConfirmRetention.Should().BeTrue();
        await viewer.ConfirmRetentionAsync();
        viewer.Status.Should().Contain("readback committed");
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.IsActive.Should().BeTrue();
        viewer.Close();
        viewer.CanConfirmRetention.Should().BeFalse();
        viewer.RetentionPreview.Should().NotContain(fixture.Request.SessionId.Value.ToString("D"));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("selection")]
    [InlineData("revision")]
    [InlineData("hold-race")]
    public async Task Native_review_commit_fences_clear_stale_previews_and_never_claim_success(string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var checkpoint = new Checkpoint();
        fixture.Reopen(checkpoint);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.PreviewRetention(keep: true);
        if (failure is "hold-race")
        {
            using var root = HostActivity.BeginRoot(
                new(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            var current = await service.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
            var kept = await service.SetRetentionHoldAsync(current, true, RequestOrigin.LocalUi, () => true, fixture.Token);
            await service.SetRetentionHoldAsync(kept, false, RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        else
        {
            checkpoint.Callback = () =>
            {
                if (failure is "close") { viewer.Close(); }
                if (failure is "privacy") { access.CanInspect = false; access.CanControl = false; }
                if (failure is "selection") { viewer.SelectWorkRecord(null); }
                if (failure is "revision") { access.ControlRevision++; }
            };
        }
        await viewer.ConfirmRetentionAsync();
        checkpoint.Callback = null;
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Perpetual.Should().BeFalse();
        viewer.Status.Should().NotContain("readback committed");
        viewer.CanConfirmRetention.Should().BeFalse();
        viewer.RetentionPreview.Should().NotContain(fixture.Request.SessionId.Value.ToString("D"));
        viewer.Close();
    }

    private sealed class Checkpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? Callback { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => Callback?.Invoke();
    }

    [WindowsFact]
    public async Task Exact_ID_status_remains_available_for_due_session_without_reading_content_or_creating_implicit_selection_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var clock = await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(fixture, access),
            new(new WindowsSqliteEvidenceReader(sink), access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        viewer.HistorySessionId = fixture.Request.SessionId.Value.ToString("D");
        viewer.CanReadExactRetention.Should().BeTrue();
        await viewer.ReadExactRetentionAsync();
        viewer.SelectedSessionRecord.Should().BeNull();
        viewer.Detail.Should().BeEmpty();
        viewer.RetentionStatus.Should().Contain("already due at observation");
        viewer.CanDone.Should().BeFalse();
        viewer.CanResume.Should().BeFalse();
        viewer.CanKeepSession.Should().BeTrue();
        viewer.PreviewRetention(keep: true);
        await viewer.ConfirmRetentionAsync();
        viewer.Status.Should().Contain("readback committed");
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock with { Perpetual = true });
        viewer.PreviewRetention(keep: false);
        viewer.HistorySessionId = Guid.NewGuid().ToString("D");
        viewer.CanConfirmRetention.Should().BeFalse();
        viewer.RetentionPreview.Should().NotContain(fixture.Request.SessionId.Value.ToString("D"));
        viewer.Close();
    }
}
