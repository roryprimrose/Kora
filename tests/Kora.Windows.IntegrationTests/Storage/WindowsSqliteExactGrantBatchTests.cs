using System.Collections.Immutable;
using System.Text.Json;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class WindowsSqliteExactGrantBatchTests
{
    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task EveryExistingScopeRevokesAsOneCompleteRetainedExactSetWithoutBroadening(string scope)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2, scope);
        var preview = await Preview(f, grants);
        await using var control = Control(f);
        var result = await control.RevokeBatchAsync(preview, static () => true, f.Token);
        result.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        result.RevokedGrants.Should().Equal(grants.Select(grant => grant.Revoke("explicit-user-revocation")));
        foreach (var revoked in result.RevokedGrants)
        {
            (await f.Store.InspectExactGrantAsync(revoked.Id, f.Token))!.Grant.Should().Be(revoked);
            (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, revoked.Id, revoked.Revision, f.Token)))
                .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
        }
    }

    [WindowsFact]
    public async Task RealNativeMultiSelectionPreviewAndPageRefreshUseStableExactIdentitiesAndRetireConfirmation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        await using var control = Control(f);
        await HeadlessSession.RunAsync(async () =>
        {
            using var state = new ExactGrantsViewModel(f.Store, control, new Access(), static () => true,
                NullLogger<ExactGrantsViewModel>.Instance);
            var window = new ExactGrantsWindow(state);
            try
            {
                window.Show();
                await state.RefreshAsync(f.Token);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Retained exact operation grants", StringComparison.Ordinal));
                list.SelectionMode.Should().Be(SelectionMode.Multiple);
                AutomationProperties.GetName(list).Should().Be("Retained exact operation grants");
                list.SelectedItems!.Add(state.Records[0]);
                list.SelectedItems.Add(state.Records[1]);
                await state.PreviewBatchAsync(f.Token);
                foreach (var grant in grants) { state.BatchInspection.Should().Contain(grant.Id.Value.ToString("D")); }
                state.BatchReviewed = true;
                state.CanRevokeBatch.Should().BeTrue();
                await state.RefreshAsync(f.Token);
                state.Records.Should().HaveCount(2);
                state.BatchReviewed.Should().BeFalse();
                state.BatchInspection.Should().Contain("Select explicit");
                state.Message.Should().NotContain("unavailable");
                list.SelectedItems!.Add(state.Records[1]);
                list.SelectedItems.Add(state.Records[0]);
                await state.PreviewBatchAsync(f.Token);
                state.BatchReviewed = true;
                await state.RevokeBatchAsync(f.Token);
                state.Message.Should().Contain("ALL selected IDs");
            }
            finally { window.Close(); }
        });
    }

    private sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
    }

    [WindowsFact]
    public async Task EntireReviewedExactSetCommitsOneTypedAuditAndRetainsUnrelatedAuthorityAndUseHistory()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 3);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[0].Id, grants[0].Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        var preview = await Preview(f, grants.Take(2));
        var before = preview.Records.Select(item => item.Grant).ToImmutableArray();
        var unrelated = (await f.Store.InspectExactGrantAsync(grants[2].Id, f.Token))!;
        var questions = f.Count("host_questions");
        await using var control = Control(f);
        var result = await control.RevokeBatchAsync(preview, static () => true, f.Token);
        result.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        result.RevokedGrants.Should().Equal(before.Select(item => item.Revoke("explicit-user-revocation")));
        foreach (var revoked in result.RevokedGrants)
        {
            (await f.Store.InspectExactGrantAsync(revoked.Id, f.Token))!.Grant.Should().Be(revoked);
            (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, revoked.Id, revoked.Revision, f.Token)))
                .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
        }
        (await f.Store.InspectExactGrantAsync(grants[2].Id, f.Token)).Should().Be(unrelated);
        f.Count("host_questions").Should().Be(questions);
        using (var raw = f.OpenRaw())
        using (var command = raw.CreateCommand())
        {
            command.CommandText = "SELECT envelope FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='approval.revoke-exact-batch';";
            using var rows = command.ExecuteReader();
            rows.Read().Should().BeTrue();
            using var json = JsonDocument.Parse(rows.GetString(0));
            var root = json.RootElement;
            root.GetProperty("RequestedGrants").GetArrayLength().Should().Be(2);
            root.GetProperty("Changes").GetArrayLength().Should().Be(2);
            root.GetProperty("RequestedAudit").GetProperty("CorrelationId").GetString()
                .Should().Be(root.GetProperty("Audit").GetProperty("CorrelationId").GetString());
            root.GetProperty("Request").GetProperty("SessionId").GetProperty("Value").GetGuid().Should().NotBe(f.Request.SessionId.Value);
            rows.Read().Should().BeFalse();
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        foreach (var revoked in result.RevokedGrants)
        {
            (await f.Store.InspectExactGrantAsync(revoked.Id, f.Token))!.Grant.Should().Be(revoked);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OneUsedRevokedOrChangedOriginRejectsEveryReviewedTargetWithoutOverwritingNewerState(int change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 3);
        var preview = await Preview(f, grants.Take(2));
        await using var control = Control(f);
        if (change == 0)
        {
            await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[1].Id, grants[1].Revision, f.Token));
        }
        else if (change == 1)
        {
            await control.RevokeAsync(preview.Records[1], static () => true, f.Token);
        }
        else
        {
            await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), active: false, remove: false, f.Token));
        }
        var current = new List<ExactGrantInspection?>();
        foreach (var grant in grants) { current.Add(await f.Store.InspectExactGrantAsync(grant.Id, f.Token)); }
        var result = await control.RevokeBatchAsync(preview, static () => true, f.Token);
        result.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        result.RevokedGrants.Should().BeEmpty();
        for (var i = 0; i < grants.Length; i++)
        {
            (await f.Store.InspectExactGrantAsync(grants[i].Id, f.Token)).Should().Be(current[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AuditWriteGrantWriteCancellationAndAdmissionFailureRollBackEverySelectedChange(int failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var preview = await Preview(f, grants);
        await using var control = Control(f);
        await control.RevokeAsync(preview.Records[0] with { Grant = grants[0] with { Revision = new(99) } }, static () => true, f.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var admitted = true;
        if (failure < 2)
        {
            checkpoint.Audit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = failure == 0
                    ? "CREATE TRIGGER fail_batch BEFORE INSERT ON security_audit_events BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;"
                    : "CREATE TRIGGER fail_batch BEFORE UPDATE ON perpetual_grants WHEN OLD.approval_id='" + grants[1].Id.Value.ToString("D") + "' BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;";
                command.ExecuteNonQuery();
            };
        }
        else { checkpoint.Commit = (_, _) => { if (failure == 2) { cancel.Cancel(); } else { admitted = false; } }; }
        var revoke = () => control.RevokeBatchAsync(preview, () => admitted, cancel.Token);
        if (failure < 2) { await revoke.Should().ThrowAsync<IOException>(); }
        else if (failure == 2) { await revoke.Should().ThrowAsync<OperationCanceledException>(); }
        else { await revoke.Should().ThrowAsync<InvalidOperationException>(); }
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        foreach (var grant in grants)
        {
            (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
        }
        using var raw = f.OpenRaw();
        using var query = raw.CreateCommand();
        query.CommandText = "SELECT count(*) FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='approval.revoke-exact-batch';";
        ((long)query.ExecuteScalar()!).Should().Be(0);
    }

    [WindowsFact]
    public async Task ConcurrentConsumeAndBatchRevocationHaveOneWinnerAndNoPartialSet()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var preview = await Preview(f, grants);
        await using var control = Control(f);
        var revoke = control.RevokeBatchAsync(preview, static () => true, f.Token);
        var consume = f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[1].Id, grants[1].Revision, f.Token));
        await Task.WhenAll(revoke, consume);
        ((revoke.Result.Outcome == HostInteractionOutcome.Revoked ? 1 : 0)
            + (consume.Result.Outcome == HostInteractionOutcome.Consumed ? 1 : 0)).Should().Be(1);
        var first = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!.Grant;
        var second = (await f.Store.InspectExactGrantAsync(grants[1].Id, f.Token))!.Grant;
        if (revoke.Result.Outcome == HostInteractionOutcome.Revoked)
        {
            first.Status.Should().Be(OperationGrantStatus.Revoked);
            second.Status.Should().Be(OperationGrantStatus.Revoked);
            first.Revision.Value.Should().Be(2);
            second.Revision.Value.Should().Be(2);
        }
        else
        {
            revoke.Result.RevokedGrants.Should().BeEmpty();
            first.Should().Be(grants[0]);
            second.UseCount.Should().Be(1);
        }
    }

    [WindowsFact]
    public async Task RemovedOriginPerpetualSetRemainsRevocableOnlyThroughFreshCurrentNativeControl()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        foreach (var task in (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token)).Records)
        {
            if (!task.IsTerminal)
            {
                using var activity = HostActivity.BeginRoot(task.Request, HostActivityLayer.Application, HostOperation.Request);
                await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token);
            }
        }
        var disposition = await f.Store.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.RunAsync(async () =>
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.DisposeSessionAsync(f.Request, disposition, static () => true, f.Token);
        });
        var preview = await Preview(f, grants);
        preview.Records.Should().OnlyContain(item => item.OriginSessionRemoved);
        await using var control = Control(f);
        (await control.RevokeBatchAsync(preview, static () => true, f.Token)).RevokedGrants.Should().HaveCount(2);
    }

    [WindowsFact]
    public async Task FreshPreviewRejectsForeignMissingDuplicateAndTerminalSelectionsAndCannotBeReplayed()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var preview = await Preview(f, grants);
        var target = preview.Records[1];
        foreach (var changed in new[]
        {
            target with { StoreIdentity = "foreign" },
            target with { Grant = target.Grant with { Id = new(Guid.NewGuid()) } },
        })
        {
            var read = () => f.Store.PreviewExactGrantBatchAsync(new(Guid.NewGuid(),
                [preview.Records[0] with { StoreIdentity = changed.StoreIdentity }, changed]), f.Token).AsTask();
            await read.Should().ThrowAsync<InvalidOperationException>();
        }
        var duplicate = () => f.Store.PreviewExactGrantBatchAsync(new(Guid.NewGuid(), [target, target]), f.Token).AsTask();
        await duplicate.Should().ThrowAsync<InvalidDataException>();
        await using var control = Control(f);
        var superseded = await Preview(f, grants.Reverse());
        (await control.RevokeBatchAsync(preview, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await control.RevokeBatchAsync(superseded, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
        (await control.RevokeBatchAsync(superseded, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    private static async Task<ImmutableArray<OperationGrant>> Grants(InteractionStorageFixture f, int count, string scope = "perpetual")
    {
        var grants = ImmutableArray.CreateBuilder<OperationGrant>();
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
                await f.AdmitAsync(newSession: false);
            }
            grants.Add(await f.GrantAsync(scope));
        }
        return grants.ToImmutable();
    }

    private static async Task<ExactGrantBatch> Preview(InteractionStorageFixture f, IEnumerable<OperationGrant> grants)
    {
        var records = ImmutableArray.CreateBuilder<ExactGrantInspection>();
        foreach (var grant in grants) { records.Add((await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!); }
        return await f.Store.PreviewExactGrantBatchAsync(new(Guid.NewGuid(), records.ToImmutable()), f.Token);
    }

    private static ExactGrantControlAdmission Control(InteractionStorageFixture f) =>
        new(f.Store, f.Store, new(f.Tasks), f.Authorization);
}