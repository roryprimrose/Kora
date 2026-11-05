using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteAuditPrefixRetentionTests
{
    [WindowsFact]
    public async Task VerifiedExpiredPrefixIsRemovedAtomicallyAndRetainedStateSurvivesRestart()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        var original = Rows(f);
        var committed = f.Time.Now;
        f.Time.Now = committed.AddDays(90).AddTicks(-1);
        (await Prune(f)).Removed.Should().Be(0);
        Rows(f).Should().Equal(original);
        f.Time.Now = committed.AddDays(90);
        var result = await Prune(f);
        result.Should().Be(new AuthorityAuditRetentionBatch(2, 2, f.Time.Now, true, true));
        Rows(f).Should().Equal(original.Skip(2));
        var state = await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token);
        state!.IsActive.Should().BeFalse();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token)).Should().Be(state);
        Rows(f).Should().Equal(original.Skip(2));
        (await Prune(f)).Removed.Should().Be(0);
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        var batch = await ((ICommittedAuthorityAuditReader)f.Store).ReadAsync(
            new() { Source = EvidenceSource.AuthorityAudit }, null, viewer.Request, f.Time.Now, f.Token);
        batch.Status.Should().Be(EvidencePageStatus.Partial);
        batch.Candidates.Select(c => c.Record.AuditSequence).Should().Equal(3);
    }

    [Theory]
    [InlineData("DELETE FROM authority_retention_anchor;")]
    [InlineData("UPDATE authority_retention_anchor SET hash=printf('%064d',1);")]
    [InlineData("UPDATE authority_retention_anchor SET sequence=0,hash=printf('%064d',0),cutoff_utc=0;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=3;")]
    public async Task MissingOrInconsistentAnchorAndRetainedRowRemovalFailClosed(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        (await Prune(f)).Removed.Should().Be(2);
        f.Mutate(mutation);
        f.Reopen();
        await FluentActions.Awaiting(() => f.Store.InitializeAsync(f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("authority")]
    [InlineData("storage")]
    public async Task FailureBeforeCommitPreservesBothOriginalRowsAndAnchor(string failure)
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        var original = Rows(f);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var admitted = true;
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        checkpoint.Commit = (_, _) =>
        {
            if (failure is "cancel") { cancellation.Cancel(); }
            else if (failure is "authority") { admitted = false; }
            else { throw new IOException("Synthetic precommit storage failure."); }
        };
        using var owner = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup);
        var prune = () => f.Store.PruneAuditPrefixAsync(() => admitted, cancellation.Token).AsTask();
        if (failure is "cancel") { await prune.Should().ThrowAsync<OperationCanceledException>(); }
        else if (failure is "authority") { await prune.Should().ThrowAsync<InvalidOperationException>(); }
        else { await prune.Should().ThrowAsync<IOException>(); }
        Rows(f).Should().Equal(original);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await Prune(f)).Removed.Should().Be(2);
    }

    [WindowsFact]
    public async Task StateProofsLiveWorkAndIndependentPerpetualGrantsHoldWithoutDeletion()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var original = Rows(f);
        f.Time.Now = f.Time.Now.AddDays(400);
        var result = await Prune(f);
        result.Removed.Should().Be(0);
        result.HasDueBacklog.Should().BeTrue();
        result.ReferenceHeld.Should().BeTrue();
        Rows(f).Should().Equal(original);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
    }

    [WindowsFact]
    public async Task MigrationPreservesOriginalAuditBytesAndDoesNotPruneOrRenewDeadlines()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        var original = Rows(f);
        f.DropEmptyAuditAnchorForLegacy();
        f.Mutate("PRAGMA user_version=14;");
        f.Time.Now = f.Time.Now.AddDays(400);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        Rows(f).Should().Equal(original);
        (await Prune(f)).Removed.Should().Be(2);
    }

    [WindowsFact]
    public async Task OriginalAuthoritySnapshotsAreRetiredByPruningEvenWhenHeadDoesNotChange()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        EvidenceReadBatch original;
        using (var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            original = await ((ICommittedAuthorityAuditReader)f.Store).ReadAsync(
                new() { Source = EvidenceSource.AuthorityAudit, Limit = 1 }, null, viewer.Request, f.Time.Now, f.Token);
        }
        f.Time.Now = f.Time.Now.AddDays(90);
        (await Prune(f)).Removed.Should().Be(2);
        using var read = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        await FluentActions.Awaiting(() => ((ICommittedAuthorityAuditReader)f.Store).ReadAsync(
            new() { Source = EvidenceSource.AuthorityAudit }, new(original.Snapshot, original.Candidates[0].Position),
            read.Request, f.Time.Now, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task ForeignOriginAndRevokedAdmissionCannotPrune()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        using (var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Windows, HostOperation.Request))
        {
            await FluentActions.Awaiting(() => f.Store.PruneAuditPrefixAsync(() => true, f.Token).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        using var owner = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup);
        await FluentActions.Awaiting(() => f.Store.PruneAuditPrefixAsync(() => false, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        Rows(f).Should().HaveCount(3);
    }

    [WindowsFact]
    public async Task BatchLimitAndRestartContinueAtExactOriginalSequenceWithoutEvictingTheHeldSuffix()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        for (var generation = 1; generation <= WindowsSqliteHostInteractionStore.MaximumAuditRetentionRecords + 2; generation++)
        {
            await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(generation),
                active: generation % 2 == 0, remove: false, f.Token));
        }
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var original = Rows(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        var first = await Prune(f);
        first.Removed.Should().Be(WindowsSqliteHostInteractionStore.MaximumAuditRetentionRecords);
        first.PrunedThrough.Should().Be(WindowsSqliteHostInteractionStore.MaximumAuditRetentionRecords);
        first.HasDueBacklog.Should().BeTrue();
        first.ReferenceHeld.Should().BeFalse();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var second = await Prune(f);
        second.Removed.Should().Be(original.Length - first.Removed - 1);
        second.PrunedThrough.Should().Be(original.Length - 1);
        second.ReferenceHeld.Should().BeTrue();
        Rows(f).Should().Equal(original.TakeLast(1));
    }

    [WindowsFact]
    public async Task UnexpiredPrefixStopsLaterDueRecordsWithNonmonotoneOriginalPolicies()
    {
        using var f = new InteractionStorageFixture();
        var policy = new AuditRetentionPolicy();
        policy.Activate(AuditRetentionDays.Default);
        f.Reopen(auditPolicy: policy);
        await f.InitializeAsync();
        policy.Activate(new(365));
        await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), false, false, f.Token));
        policy.Activate(new(30));
        await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(2), true, false, f.Token));
        await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(3), false, false, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var original = Rows(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        var result = await Prune(f);
        result.Removed.Should().Be(2);
        result.HasDueBacklog.Should().BeTrue();
        result.ReferenceHeld.Should().BeFalse();
        Rows(f).Should().Equal(original.Skip(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCancellationDoesNotUndoCommitAndPostcommitFailureRequiresInspection(bool fail)
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        var original = Rows(f);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var checkpoint = new InteractionTransactionCheckpoint
        {
            AuditRetentionCommitted = _ =>
            {
                if (fail) { throw new IOException("Synthetic postcommit readback failure."); }
                cancellation.Cancel();
            },
        };
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        f.Time.Now = f.Time.Now.AddDays(90);
        using var owner = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup);
        var prune = () => f.Store.PruneAuditPrefixAsync(() => true, cancellation.Token).AsTask();
        if (fail) { await prune.Should().ThrowAsync<IOException>(); }
        else { (await prune()).Removed.Should().Be(2); }
        Rows(f).Should().Equal(original.Skip(2));
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await Prune(f)).Removed.Should().Be(0);
    }

    [WindowsFact]
    public async Task AppendsAfterPruningExtendOriginalHeadWithoutResurrectingExpiredPrefix()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        (await Prune(f)).PrunedThrough.Should().Be(2);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.RunAsync(async () =>
        {
            await f.Tasks.CommitAsync(new(f.Request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
            await f.Store.SetSessionLifecycleAsync(f.Request, new(2), true, false, f.Token);
        });
        Rows(f).Should().HaveCount(2);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token))!.Generation.Value.Should().Be(3);
        (await Prune(f)).HasDueBacklog.Should().BeFalse();
    }

    [WindowsFact]
    public async Task ImpactGrantHistoryAndAttachmentCatalogueReadTheVerifiedPostexpirySuffix()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await f.RunAsync(async () => await f.Store.SetPerpetualAsync(f.Request, new(1), true, () => true, f.Token));
        await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), false, false, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        f.Time.Now = f.Time.Now.AddDays(90);
        (await Prune(f)).Removed.Should().Be(2);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var impact = f.Store.ReadAuditImpact(new(30), f.Time.Now.AddDays(40), f.Token);
        impact.Status.Should().Be(AuditImpactStatus.Complete);
        impact.Aggregate!.AffectedCount.Should().BeGreaterThan(0);
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        var history = await f.Store.ReadExactGrantHistoryAsync(exact, viewer.Request, new(), 1, Guid.NewGuid(),
            null, () => true, f.Token);
        history.Records.Should().ContainSingle().Which.Audit.ActionId.Should().Be("approval.confirm");
        var catalogue = await f.Store.ReadAttachmentPage(new(), null, () => true, f.Token);
        catalogue.Records.Should().BeEmpty();
        catalogue.AuthorityCeiling.Should().BeGreaterThan(2);
    }

    [WindowsFact]
    public async Task ClockRollbackAfterAConfirmedBoundaryHoldsFurtherPruning()
    {
        using var f = new InteractionStorageFixture();
        await Prepare(f);
        f.Time.Now = f.Time.Now.AddDays(90);
        (await Prune(f)).Removed.Should().Be(2);
        var original = Rows(f);
        f.Time.Now = f.Time.Now.AddTicks(-1);
        await FluentActions.Awaiting(() => Prune(f)).Should().ThrowAsync<InvalidOperationException>();
        Rows(f).Should().Equal(original);
    }

    private static async Task Prepare(InteractionStorageFixture f)
    {
        await f.InitializeAsync();
        await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), active: false, remove: false, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
    }

    private static async Task<AuthorityAuditRetentionBatch> Prune(InteractionStorageFixture f)
    {
        using var owner = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup);
        return await f.Store.PruneAuditPrefixAsync(() => true, f.Token);
    }

    private static string[] Rows(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,hash,envelope FROM security_audit_events ORDER BY sequence;";
        using var rows = command.ExecuteReader();
        var values = new List<string>();
        while (rows.Read()) { values.Add($"{rows.GetInt64(0)}:{rows.GetString(1)}:{rows.GetString(2)}"); }
        return values.ToArray();
    }
}
