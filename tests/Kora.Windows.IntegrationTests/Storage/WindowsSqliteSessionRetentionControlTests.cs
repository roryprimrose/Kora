using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionRetentionControlTests
{
    [WindowsFact]
    public async Task Native_keep_and_ordinary_control_in_Done_session_preserve_clock_grants_history_and_unrelated_state()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        using var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var unrelated = await service.CreateAsync(new("Unrelated"), RequestOrigin.LocalUi, fixture.Token);
        var other = await fixture.Store.ReadRetentionObservationAsync(unrelated.Authority.SessionId, fixture.Token);
        var done = await service.ChangeLifecycleAsync(id, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        var before = await service.ReadRetentionAsync(id, fixture.Token);
        before.Generation.Should().Be(done.Generation);
        fixture.Time.Now = before.State.DeleteDue.AddDays(1);
        var kept = await service.SetRetentionHoldAsync(before, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        kept.State.Should().Be(before.State with { Perpetual = true });
        kept.ExemptionAuditSequence.Should().BeGreaterThan(before.ExemptionAuditSequence);
        var ordinary = await service.SetRetentionHoldAsync(kept, false, RequestOrigin.LocalUi, () => true, fixture.Token);
        ordinary.State.Should().Be(before.State);
        ordinary.ExemptionAuditSequence.Should().BeGreaterThan(kept.ExemptionAuditSequence);
        (await fixture.Store.ReadSessionAsync(id, fixture.Token))!.Should().Be(done);
        (await fixture.Store.ReadRetentionObservationAsync(unrelated.Authority.SessionId, fixture.Token)).Should()
            .Be(other with { ObservedAt = fixture.Time.Now });
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(grant);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token)).Should().Be(ordinary);
    }

    [Theory]
    [InlineData("hold-aba")]
    [InlineData("activity")]
    [InlineData("archive")]
    [InlineData("removed")]
    [InlineData("purged")]
    public async Task Atomic_expected_state_and_audit_identity_reject_late_review_without_overwriting_new_authority(string race)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        using var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var displayed = await service.ReadRetentionAsync(id, fixture.Token);
        if (race is "hold-aba")
        {
            var kept = await service.SetRetentionHoldAsync(displayed, true, RequestOrigin.LocalUi, () => true, fixture.Token);
            await service.SetRetentionHoldAsync(kept, false, RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        if (race is "activity")
        {
            fixture.Time.Now = fixture.Time.Now.AddHours(1);
            fixture.Request = InteractionStorageFixture.NewRequest(id);
            await fixture.AdmitAsync(newSession: false);
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        }
        if (race is "archive" or "purged")
        {
            fixture.Time.Now = race is "archive" ? displayed.State.ArchiveDue : displayed.State.DeleteDue;
            await ApplyAsync(fixture);
        }
        if (race is "removed")
        {
            var preview = await service.PreviewDispositionAsync(id, displayed.Generation, 0, fixture.Token);
            await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        var current = await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token);
        var auditCount = fixture.Count("security_audit_events");
        var late = () => service.SetRetentionHoldAsync(displayed, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        await late.Should().ThrowAsync<InvalidOperationException>();
        fixture.Count("security_audit_events").Should().Be(auditCount);
        (await fixture.Store.ReadRetentionObservationAsync(id, fixture.Token)).Should().BeEquivalentTo(current);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("ownership")]
    [InlineData("corrupt")]
    [InlineData("foreign")]
    public async Task Failure_before_commit_preserves_hold_and_never_fabricates_audit_success(string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var displayed = await fixture.Store.ReadRetentionObservationAsync(fixture.Request.SessionId, fixture.Token);
        fixture.Request = new(new(Guid.NewGuid()), displayed.State.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        await fixture.RunAsync(() => fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        using var cancellation = new CancellationTokenSource();
        if (failure is "audit" or "cancel" or "ownership")
        {
            fixture.Reopen(new Checkpoint(() =>
            {
                if (failure is "audit") { throw new IOException("Audit transaction unavailable."); }
                if (failure is "cancel") { cancellation.Cancel(); }
            }, beforeAudit: failure is "audit"));
        }
        if (failure is "corrupt") { fixture.Mutate("UPDATE session_retention SET last_activity='invalid';"); }
        if (failure is "foreign")
        {
            displayed = displayed with { State = displayed.State with { SessionId = new(Guid.NewGuid()) } };
        }
        var audits = fixture.Count("security_audit_events");
        var expected = displayed;
        var action = () => fixture.RunAsync(() => fixture.Store.SetRetentionHoldAsync(fixture.Request, expected, true,
            () => failure is not "ownership", cancellation.Token));
        await action.Should().ThrowAsync<Exception>();
        fixture.Count("security_audit_events").Should().Be(audits);
        if (failure is not "corrupt")
        {
            (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Perpetual.Should().BeFalse();
        }
    }

    [WindowsFact]
    public async Task Passive_maintenance_admission_never_cleans_or_renews_and_due_or_unknown_inventory_denies_content()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var id = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        var audits = fixture.Count("security_audit_events");
        await fixture.Store.ValidatePassiveInspectionAsync(fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        // Current original work holds, so no synthetic cancellation or cleanup is allowed.
        await fixture.Store.ValidatePassiveInspectionAsync(fixture.Token);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Time.Now = clock.LastMeaningfulActivity;
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        fixture.Time.Now = clock.ArchiveDue;
        var inspect = () => fixture.Store.ValidatePassiveInspectionAsync(fixture.Token).AsTask();
        await inspect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*due*");
        (await fixture.Store.ReadSessionAsync(id, fixture.Token))!.IsActive.Should().BeTrue();
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        var artifactDirectory = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath)!, "Artifacts");
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "unregistered-copy"), "fixture", fixture.Token);
        await inspect.Should().ThrowAsync<InvalidDataException>().WithMessage("*inventoried*");
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
    }

    private static async Task ApplyAsync(InteractionStorageFixture fixture)
    {
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Retention);
        await fixture.Store.ApplyRetentionAsync(() => true, static (_, _) => ValueTask.CompletedTask, fixture.Token);
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Unknown)]
    public async Task Returning_ordinary_policy_never_bypasses_live_or_unknown_work_holds(HostTaskState state)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        if (state != HostTaskState.IntentRecorded)
        {
            await fixture.RunAsync(() => fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.DispatchRecorded), 1, fixture.Token).AsTask());
            if (state == HostTaskState.Unknown)
            {
                await fixture.RunAsync(() => fixture.Tasks.CommitAsync(new(fixture.Request, new(3), state), 2, fixture.Token).AsTask());
            }
        }
        using var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var before = await service.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
        var kept = await service.SetRetentionHoldAsync(before, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        fixture.Time.Now = before.State.DeleteDue.AddDays(1);
        var ordinary = await service.SetRetentionHoldAsync(kept, false, RequestOrigin.LocalUi, () => true, fixture.Token);
        ordinary.State.Should().Be(before.State);
        ordinary.WorkHoldObserved.Should().BeTrue();
        using var maintenance = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Retention);
        (await fixture.Store.ApplyRetentionAsync(() => true, static (_, _) => ValueTask.CompletedTask, fixture.Token))
            .Should().Be(new SessionRetentionBatch(0, 0, 1, false));
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(before.State);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task Private_store_rejects_non_native_and_invocation_lineage_without_mutation(RequestOrigin origin, bool invocation)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var before = await fixture.Store.ReadRetentionObservationAsync(fixture.Request.SessionId, fixture.Token);
        fixture.Request = new(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), origin,
            invocation ? new(Guid.NewGuid()) : null);
        await fixture.RunAsync(() => fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        var action = () => fixture.RunAsync(() => fixture.Store.SetRetentionHoldAsync(fixture.Request, before, true,
            () => true, fixture.Token));
        await action.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(before.State);
    }

    private sealed class Checkpoint(Action action, bool beforeAudit = false) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (beforeAudit) { action(); }
        }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (!beforeAudit) { action(); }
        }
    }
}
