using System.Diagnostics;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteExactGrantTests
{
    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task NativeCurrentExactRevocationRetainsImmutableMetadataAndDeniesSubsequentConsumes(string scope)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync(scope);
        var before = await f.Store.InspectExactGrantAsync(grant.Id, f.Token);
        var questionCount = f.Count("host_questions");
        await using var control = Control(f);
        var result = await control.RevokeAsync(before!, static () => true, f.Token);
        result.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        var retained = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant;
        retained.Should().Be(grant with { Revision = new(2), Status = OperationGrantStatus.Revoked, RevocationReason = "explicit-user-revocation" });
        f.Count("host_questions").Should().Be(questionCount);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, retained.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var stale = await control.RevokeAsync(before!, static () => true, f.Token);
        stale.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(retained);
        using var connection = f.OpenRaw();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT envelope FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='approval.revoke-exact' ORDER BY sequence;";
        using var rows = query.ExecuteReader();
        var audits = new List<JsonDocument>();
        try
        {
            while (rows.Read()) { audits.Add(JsonDocument.Parse(rows.GetString(0))); }
            audits.Should().HaveCount(2);
            foreach (var item in audits)
            {
                var root = item.RootElement;
                var requested = root.GetProperty("RequestedAudit");
                var terminal = root.GetProperty("Audit");
                requested.GetProperty("Outcome").GetInt32().Should().Be(0);
                requested.GetProperty("CorrelationId").GetString().Should().Be(terminal.GetProperty("CorrelationId").GetString());
                root.GetProperty("Request").GetProperty("SessionId").GetProperty("Value").GetGuid().Should().NotBe(f.Request.SessionId.Value);
                root.GetProperty("TraceId").GetString().Should().HaveLength(32);
                root.GetProperty("SpanId").GetString().Should().HaveLength(16);
            }
        }
        finally { foreach (var item in audits) { item.Dispose(); } }
    }

    [WindowsFact]
    public async Task IndependentPerpetualOriginRemovalDoesNotSupplyControlAuthorityOrHideRetainedRecord()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var perpetual = await f.GrantAsync("perpetual");
        var old = await f.Store.InspectExactGrantAsync(perpetual.Id, f.Token);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.AdmitAsync(newSession: false);
        var once = await f.GrantAsync("once");
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
        (await f.Store.InspectExactGrantAsync(once.Id, f.Token)).Should().BeNull();
        var current = (await f.Store.InspectExactGrantAsync(perpetual.Id, f.Token))!;
        current.OriginSessionRemoved.Should().BeTrue();
        current.Grant.Should().Be(perpetual);
        await using var control = Control(f);
        (await control.RevokeAsync(old!, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await control.RevokeAsync(current, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.InspectExactGrantAsync(perpetual.Id, f.Token))!.Grant.Status.Should().Be(OperationGrantStatus.Revoked);
        (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records.Should().ContainSingle();
    }

    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task RevokeAndConsumeHaveOneSerializableWinnerForTheExactDisplayedRevision(string scope)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync(scope);
        var preview = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        await using var control = Control(f);
        var revoke = control.RevokeAsync(preview, static () => true, f.Token);
        var consume = f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token));
        await Task.WhenAll(revoke, consume);
        ((revoke.Result.Outcome == HostInteractionOutcome.Revoked ? 1 : 0)
            + (consume.Result.Outcome == HostInteractionOutcome.Consumed ? 1 : 0)).Should().Be(1);
        var current = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        current.Grant.Revision.Value.Should().Be(2);
        current.Grant.UseCount.Should().Be(consume.Result.Outcome == HostInteractionOutcome.Consumed ? 1 : 0);
        if (current.Grant.Status == OperationGrantStatus.Active)
        {
            (await control.RevokeAsync(current, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
        }
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, new(3), f.Token)))
            .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
    }

    [WindowsFact]
    public async Task BoundedInventoryCursorsReadsAndMissingIdsNeverCreateIntentActivityOrEffectAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var ids = new List<Guid>();
        for (var index = 0; index < 51; index++)
        {
            if (index != 0)
            {
                f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
                await f.AdmitAsync(newSession: false);
            }
            ids.Add((await f.GrantAsync(index % 3 == 0 ? "perpetual" : index % 3 == 1 ? "session" : "once")).Id.Value);
        }
        var events = f.Count("security_audit_events");
        var tasks = f.Count("host_task_events");
        Activity.Current.Should().BeNull();
        var page = await f.Store.ReadExactGrantPageAsync(null, f.Token);
        page.Next.Should().NotBeNull();
        var all = new List<Guid>();
        var firstCursor = page.Next!;
        for (var index = 0; index < 8; index++)
        {
            page.Records.Length.Should().BeInRange(1, ExactGrantPage.MaximumRecords);
            page.Records.Sum(record => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record))).Should().BeLessThanOrEqualTo(ExactGrantPage.MaximumBytes);
            all.AddRange(page.Records.Select(record => record.Grant.Id.Value));
            if (page.Next is null) { break; }
            page = await f.Store.ReadExactGrantPageAsync(page.Next, f.Token);
        }
        all.Should().Equal(ids.OrderBy(id => id.ToString("D"), StringComparer.Ordinal));
        (await f.Store.InspectExactGrantAsync(new(Guid.NewGuid()), f.Token)).Should().BeNull();
        Activity.Current.Should().BeNull();
        f.Count("security_audit_events").Should().Be(events);
        f.Count("host_task_events").Should().Be(tasks);
        var foreign = () => f.Store.ReadExactGrantPageAsync(firstCursor with { StoreIdentity = "foreign" }, f.Token).AsTask();
        await foreign.Should().ThrowAsync<InvalidDataException>();
        f.Reopen();
        var uninitialized = () => f.Store.ReadExactGrantPageAsync(null, f.Token).AsTask();
        await uninitialized.Should().ThrowAsync<InvalidDataException>();
        await f.Store.InitializeAsync(f.Token);
        var expired = () => f.Store.ReadExactGrantPageAsync(firstCursor, f.Token).AsTask();
        await expired.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AuditFailureCancellationAndPrivacyChangeRollBackGrantAndAuditTogether(int failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await f.PublishAsync();
        await using var control = Control(f);
        var preview = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        // Prime the fresh control session without revoking an unrelated or terminal record.
        (await control.RevokeAsync(preview with { Grant = grant with { Revision = new(99) } }, static () => true, f.Token))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var count = f.Count("security_audit_events");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var admitted = true;
        if (failure == 0)
        {
            checkpoint.Audit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "CREATE TRIGGER fail_exact_audit BEFORE INSERT ON security_audit_events BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;";
                command.ExecuteNonQuery();
            };
        }
        else { checkpoint.Commit = (_, _) => { if (failure == 1) { cancellation.Cancel(); } else { admitted = false; } }; }
        var revoke = () => control.RevokeAsync(preview, () => admitted, cancellation.Token);
        if (failure == 0) { await revoke.Should().ThrowAsync<IOException>(); }
        else if (failure == 1) { await revoke.Should().ThrowAsync<OperationCanceledException>(); }
        else { await revoke.Should().ThrowAsync<InvalidOperationException>(); }
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
        f.Count("security_audit_events").Should().BeGreaterThanOrEqualTo(count);
        // Failed task outcome is a separate truthful audit, not a revocation terminal success.
        using var raw = f.OpenRaw();
        using var query = raw.CreateCommand();
        query.CommandText = "SELECT count(*) FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='approval.revoke-exact';";
        ((long)query.ExecuteScalar()!).Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AmbientNonNativeOrOldRequestCannotBeConvertedIntoFreshOriginalUserControl(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("once");
        var preview = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        await using var control = Control(f);
        using var ambient = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var revoke = () => control.RevokeAsync(preview, static () => true, f.Token);
        await revoke.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
    }

    [Theory]
    [InlineData("UPDATE perpetual_grants SET payload='{}';")]
    [InlineData("UPDATE perpetual_grants SET revision=99;")]
    [InlineData("UPDATE perpetual_grants SET payload=printf('%.*c',17000,'x');")]
    [InlineData("DELETE FROM perpetual_grants; DELETE FROM authority_head;")]
    public async Task MalformedSavedRowsFailClosedInsteadOfReturningAnEmptySuccess(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        f.Mutate(corruption);
        var read = () => f.Store.ReadExactGrantPageAsync(null, f.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        var inspect = () => f.Store.InspectExactGrantAsync(grant.Id, f.Token).AsTask();
        await inspect.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task ConsumedOnceAndUnrelatedExactRecordsRemainRetainedAndCannotBeRestoredOrBulkRevoked()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var once = await f.GrantAsync("once");
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, once.Id, once.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        var consumed = (await f.Store.InspectExactGrantAsync(once.Id, f.Token))!;
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.AdmitAsync(newSession: false);
        var unrelated = await f.GrantAsync("perpetual");
        await using var control = Control(f);
        (await control.RevokeAsync(consumed, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.Store.InspectExactGrantAsync(once.Id, f.Token)).Should().Be(consumed);
        (await f.Store.InspectExactGrantAsync(unrelated.Id, f.Token))!.Grant.Should().Be(unrelated);
        var foreign = consumed with { Grant = consumed.Grant with { Id = new(Guid.NewGuid()) } };
        (await control.RevokeAsync(foreign, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records.Should().HaveCount(2);
    }

    [WindowsFact]
    public async Task NormalSessionOrWrongGenerationCannotImpersonateThisRunsExactNativeControlSession()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var preview = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var normal = () => f.RunAsync(() => f.Authorization.RevokeExactAsync(f.Request, new(1), preview, static () => true, f.Token));
        await normal.Should().ThrowAsync<InvalidOperationException>();
        f.Request = InteractionStorageFixture.NewRequest();
        await f.RunAsync(async () => await f.Store.RecordControlIntentAsync(f.Request, f.Token));
        await f.RunAsync(() => f.Store.CreateExactGrantControlSessionAsync(f.Request, static () => true, f.Token));
        var wrong = () => f.RunAsync(() => f.Authorization.RevokeExactAsync(f.Request, new(2), preview, static () => true, f.Token));
        await wrong.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var retiredRun = () => f.RunAsync(() => f.Authorization.RevokeExactAsync(f.Request, new(1), preview, static () => true, f.Token));
        await retiredRun.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ExactGrantControlAdmission Control(InteractionStorageFixture f) =>
        new(f.Store, f.Store, new(f.Tasks), f.Authorization);
}
