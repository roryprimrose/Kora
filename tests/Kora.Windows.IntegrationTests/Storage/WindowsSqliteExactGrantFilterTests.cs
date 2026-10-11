using System.Diagnostics;
using System.Globalization;
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
public sealed class WindowsSqliteExactGrantFilterTests
{
    [WindowsFact]
    public async Task ExactMatchBeyondFirstFiftyStoredRowsHasEmptyBoundedPageThenTruthfulContinuationWithoutWrites()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = new List<OperationGrant>();
        for (var index = 0; index < 60; index++)
        {
            f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
            await f.AdmitAsync(newSession: false);
            f.Proposal = ProposalWithAction(f, "read." + index.ToString("d3", CultureInfo.InvariantCulture));
            await f.PublishAsync();
            grants.Add(await f.GrantAsync(index % 3 == 0 ? "perpetual" : index % 3 == 1 ? "session" : "once"));
        }
        var sorted = grants.OrderBy(grant => grant.Id.Value.ToString("D"), StringComparer.Ordinal).ToArray();
        var target = sorted[^1];
        var query = new ExactGrantQuery(target.Scope, target.Status, target.ApprovedProposal.Binding.ActionId,
            target.ApprovedProposal.Request.SessionId);
        var auditCount = f.Count("security_audit_events");
        var tasks = f.Count("host_task_events");
        var history = f.Count("session_history");
        var first = await f.Store.ReadExactGrantPageAsync(query, null, f.Token);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(51);
        first.OmittedRecords.Should().Be(50);
        first.ScannedThrough.Should().Be(sorted[49].Id.Value);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        first.Next!.After.Should().Be(sorted[49].Id.Value);
        first.Next.QueryDigest.Should().Be(query.Digest);
        var second = await f.Store.ReadExactGrantPageAsync(query, first.Next, f.Token);
        second.Records.Should().ContainSingle().Which.Grant.Should().Be(target);
        second.ScannedRows.Should().Be(10);
        second.OmittedRecords.Should().Be(9);
        second.Next.Should().BeNull();
        second.ScanLimitReached.Should().BeFalse();
        second.ScannedThrough.Should().Be(target.Id.Value);
        var all = new List<OperationGrant>();
        ExactGrantCursor? cursor = null;
        do
        {
            var unfiltered = await f.Store.ReadExactGrantPageAsync(cursor, f.Token);
            var explicitAny = await f.Store.ReadExactGrantPageAsync(ExactGrantQuery.Unfiltered, cursor, f.Token);
            explicitAny.Should().BeEquivalentTo(unfiltered);
            // The matching Active query has its own signed continuation but must keep the same byte-deferred keyset.
            if (cursor is null)
            {
                var active = await f.Store.ReadExactGrantPageAsync(new(status: OperationGrantStatus.Active), null, f.Token);
                active.Records.Should().Equal(unfiltered.Records);
                active.Records.Length.Should().BeLessThan(50);
                active.Next!.After.Should().Be(active.Records[^1].Grant.Id.Value);
                var continued = await f.Store.ReadExactGrantPageAsync(new(status: OperationGrantStatus.Active), active.Next, f.Token);
                continued.Records[0].Grant.Should().Be(sorted[active.Records.Length]);
            }
            unfiltered.Records.Length.Should().BeLessThanOrEqualTo(50);
            unfiltered.ScannedRows.Should().BeLessThanOrEqualTo(51);
            unfiltered.Records.Sum(item => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(item)))
                .Should().BeLessThanOrEqualTo(64 * 1024);
            unfiltered.OmittedRecords.Should().Be(0);
            all.AddRange(unfiltered.Records.Select(item => item.Grant));
            cursor = unfiltered.Next;
        } while (cursor is not null);
        all.Should().Equal(sorted);
        f.Count("security_audit_events").Should().Be(auditCount);
        f.Count("host_task_events").Should().Be(tasks);
        f.Count("session_history").Should().Be(history);
        Activity.Current.Should().BeNull();

        foreach (var tampered in new[]
        {
            first.Next with { StoreIdentity = "foreign" },
            first.Next with { AuditSequence = first.Next.AuditSequence + 1 },
            first.Next with { AuditHash = new string('b', 64) },
            first.Next with { After = Guid.Empty },
            first.Next with { After = sorted[0].Id.Value },
            first.Next with { QueryDigest = ExactGrantQuery.Unfiltered.Digest },
            first.Next with { Proof = "" },
            first.Next with { Proof = new string('x', 64) },
        })
        {
            var read = () => f.Store.ReadExactGrantPageAsync(query, tampered, f.Token).AsTask();
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        var crossQuery = () => f.Store.ReadExactGrantPageAsync(ExactGrantQuery.Unfiltered, first.Next, f.Token).AsTask();
        await crossQuery.Should().ThrowAsync<InvalidDataException>();
        var missingQuery = () => f.Store.ReadExactGrantPageAsync(null!, first.Next, f.Token).AsTask();
        await missingQuery.Should().ThrowAsync<ArgumentNullException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelRead = () => f.Store.ReadExactGrantPageAsync(query, first.Next, cancelled.Token).AsTask();
        await cancelRead.Should().ThrowAsync<OperationCanceledException>();
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.AdmitAsync(newSession: false);
        var stale = () => f.Store.ReadExactGrantPageAsync(query, first.Next, f.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Store.ReadExactGrantPageAsync(query, null, f.Token);
        f.Reopen();
        var uninitialized = () => f.Store.ReadExactGrantPageAsync(query, fresh.Next, f.Token).AsTask();
        await uninitialized.Should().ThrowAsync<InvalidDataException>();
        await f.Store.InitializeAsync(f.Token);
        var crossStore = () => f.Store.ReadExactGrantPageAsync(query, fresh.Next, f.Token).AsTask();
        await crossStore.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task RealApprovedConsumedAndRevokedRowsCombineExactStoredPredicatesWithoutInferringSourceSkillOrSessionAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var origin = f.Request.SessionId;
        var otherOrigin = new HostId<SessionIdentity>(Guid.NewGuid());
        var retained = new List<ExactGrantInspection>();
        await using var control = Control(f);
        for (var index = 0; index < 9; index++)
        {
            f.Request = InteractionStorageFixture.NewRequest(index % 2 == 0 ? origin : otherOrigin);
            await f.AdmitAsync(newSession: index == 1);
            f.Proposal = ProposalWithAction(f, index % 2 == 0 ? "bounded.read" : "read.i",
                index % 3 == 0 ? "builtin" : "bounded.read");
            await f.PublishAsync();
            var grant = await f.GrantAsync(index / 3 == 0 ? "once" : index / 3 == 1 ? "session" : "perpetual");
            if (index % 3 == 1)
            {
                (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token)))
                    .Outcome.Should().Be(HostInteractionOutcome.Consumed);
            }
            else if (index % 3 == 2)
            {
                var preview = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
                (await control.RevokeAsync(preview, static () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
            }
            retained.Add((await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!);
        }
        var auditCount = f.Count("security_audit_events");
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            foreach (var item in retained)
            {
                var grant = item.Grant;
                var query = new ExactGrantQuery(grant.Scope, grant.Status, grant.ApprovedProposal.Binding.ActionId,
                    grant.ApprovedProposal.Request.SessionId);
                var page = await f.Store.ReadExactGrantPageAsync(query, null, f.Token);
                page.Records.Should().Equal(retained.Where(record => query.Matches(record.Grant))
                    .OrderBy(record => record.Grant.Id.Value.ToString("D"), StringComparer.Ordinal));
                page.ScannedRows.Should().Be(9);
                page.OmittedRecords.Should().Be(9 - page.Records.Length);
                page.Next.Should().BeNull();
            }
        }
        finally { CultureInfo.CurrentCulture = prior; }
        (await f.Store.ReadExactGrantPageAsync(new(actionId: "builtin"), null, f.Token)).Records.Should().BeEmpty();
        (await f.Store.ReadExactGrantPageAsync(new(originatingSession: new(Guid.NewGuid())), null, f.Token)).Records.Should().BeEmpty();
        (await f.Store.ReadExactGrantPageAsync(new(status: OperationGrantStatus.Consumed), null, f.Token)).Records
            .Should().ContainSingle().Which.Grant.Scope.Should().Be(OperationGrantScope.Once);
        f.Count("security_audit_events").Should().Be(auditCount);
        Activity.Current.Should().BeNull();
    }

    [Theory]
    [InlineData("UPDATE perpetual_grants SET payload='{}';")]
    [InlineData("UPDATE perpetual_grants SET payload=printf('%.*c',17000,'x');")]
    [InlineData("UPDATE perpetual_grants SET revision=99;")]
    [InlineData("UPDATE perpetual_grants SET approval_id=upper(approval_id);")]
    [InlineData("UPDATE perpetual_grants SET payload=json_set(payload,'$.Status',99);")]
    [InlineData("UPDATE perpetual_grants SET payload=json_set(payload,'$.Scope',99);")]
    [InlineData("UPDATE perpetual_grants SET payload=json_set(payload,'$.ApprovedProposal.Binding.ActionId','BOUNDED.read');")]
    [InlineData("UPDATE perpetual_grants SET payload=json_set(payload,'$.ApprovedProposal.Binding.SourcePartition','other');")]
    [InlineData("UPDATE perpetual_grants SET audit_sequence=0;")]
    [InlineData("UPDATE work_sessions SET generation=99;")]
    [InlineData("UPDATE security_audit_events SET envelope='{}';")]
    [InlineData("INSERT INTO scoped_grants(approval_id,session_id,revision,payload,audit_sequence) SELECT approval_id,(SELECT session_id FROM work_sessions LIMIT 1),revision,payload,audit_sequence FROM perpetual_grants;")]
    public async Task FilteredOutMalformedOversizedAliasDuplicateUnknownAndProvenanceCorruptionStillFailClosed(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await f.GrantAsync("perpetual");
        f.Mutate(corruption);
        var read = () => f.Store.ReadExactGrantPageAsync(new(scope: OperationGrantScope.Once, actionId: "never.matches"),
            null, f.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task PerpetualOriginalSessionFilterSurvivesOriginRemovalWithoutRecreatingSessionOrGrantAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
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
        var auditCount = f.Count("security_audit_events");
        var sessions = f.Count("work_sessions");
        var page = await f.Store.ReadExactGrantPageAsync(new(OperationGrantScope.Perpetual, OperationGrantStatus.Active,
            "bounded.read", grant.ApprovedProposal.Request.SessionId), null, f.Token);
        page.Records.Should().ContainSingle().Which.Grant.Should().Be(grant);
        page.Records[0].OriginSessionRemoved.Should().BeTrue();
        page.Next.Should().BeNull();
        f.Count("security_audit_events").Should().Be(auditCount);
        f.Count("work_sessions").Should().Be(sessions);
    }

    private static ExactGrantControlAdmission Control(InteractionStorageFixture f) =>
        new(f.Store, f.Store, new(f.Tasks), f.Authorization);

    private static HostOperationProposal ProposalWithAction(InteractionStorageFixture f, string action, string source = "builtin")
    {
        var b = InteractionStorageFixture.Binding(source: source);
        var binding = new ExactOperationBinding(action, b.SourcePartition, b.SkillId, b.DefinitionDigest,
            b.DeclaredResourceDigest, b.TrackedContentDigest, b.ImplementationDigest, b.InvocationDigest,
            b.ResourceDigest, b.IdentityDigest, b.DestinationDigest, b.TransformationDigest, b.PolicyRevision);
        return new(f.Request, new(Guid.NewGuid()), new(1), binding, HostOperationEffect.BoundedRead, f.Time.Now.AddHours(1));
    }
}
