using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData(AttachmentCopyStatus.Confirmed)]
    [InlineData(AttachmentCopyStatus.Held)]
    [InlineData(AttachmentCopyStatus.Unavailable)]
    public async Task VersionCatalogueRequiresCertificationAndNeverReadsBodiesOrReconstructsRemovedVersions(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("SYNTHETIC_VERSION_BODY");
        var retained = await Attach(fixture, bytes);
        if (copy != AttachmentCopyStatus.Confirmed)
        {
            var removal = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
            await NewControl(fixture, retained.Session);
            if (copy == AttachmentCopyStatus.Unavailable)
            { await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token)); }
            else
            {
                fixture.Reopen(new AttachmentCheckpoint { Copies = () => throw new IOException("Held certification.") });
                await fixture.Store.InitializeAsync(fixture.Token);
                var replace = () => Replace(fixture, removal, bytes);
                await replace.Should().ThrowAsync<IOException>();
            }
        }
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        var original = (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Single();
        var tasks = fixture.Count("host_tasks");
        var audit = fixture.Count("security_audit_events");
        checkpoint.Metadata = command =>
        {
            using var projection = command.ExecuteReader();
            projection.FieldCount.Should().Be(6);
            Enumerable.Range(0, projection.FieldCount).Select(projection.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("SessionGeneration").And.NotContain("CapturedGeneration").And.NotContain("LIKE");
        };
        if (copy != AttachmentCopyStatus.Unavailable) { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        foreach (var query in new[] {
            new SessionAttachmentQuery(true, minimumSessionGeneration: original.SessionGeneration.Value),
            new(true, maximumSessionGeneration: original.SessionGeneration.Value),
            new(true, minimumCapturedGeneration: original.CapturedGeneration.Value),
            new(true, maximumCapturedGeneration: original.CapturedGeneration.Value),
            new(true, minimumStorageRevision: original.StorageRevision.Value),
            new(true, maximumStorageRevision: original.StorageRevision.Value),
            VersionQuery(original) })
        {
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(1);
            page.Records.Length.Should().Be(copy == AttachmentCopyStatus.Confirmed ? 1 : 0);
            page.Next.Should().BeNull();
        }
        if (copy == AttachmentCopyStatus.Unavailable)
        {
            var node = JsonSerializer.SerializeToNode(original)!;
            node["SessionGeneration"].Should().BeNull();
            node["CapturedGeneration"].Should().BeNull();
            node["StorageRevision"].Should().BeNull();
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audit);
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Generation.Value',0);");
        var invalid = () => fixture.Store.ReadAttachmentPage(new(true, minimumStorageRevision: long.MaxValue),
            null, () => true, fixture.Token).AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task VersionContinuationsBindEveryEndpointAndRetainCancellationRevocationAndAuthorityFences()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("synthetic version"));
        var row = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = VersionQuery(row);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().Contain("CorrelationBinding").And.NotContain("MinimumSessionGeneration")
            .And.NotContain("MaximumSessionGeneration").And.NotContain("MinimumCapturedGeneration")
            .And.NotContain("MaximumCapturedGeneration").And.NotContain("MinimumStorageRevision").And.NotContain("MaximumStorageRevision");
        for (var endpoint = 0; endpoint < 6; endpoint++)
        foreach (var remove in new[] { false, true })
        {
            var mismatch = () => fixture.Store.ReadAttachmentPage(VersionQuery(row, endpoint, remove),
                first.Next, () => true, fixture.Token).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancellation.Token).AsTask();
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
        }
        var revoked = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => false, fixture.Token).AsTask();
        await revoked.Should().ThrowAsync<InvalidOperationException>();
        var complete = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        complete.Records.Should().ContainSingle().Which.Should().Be(row);
        complete.Next.Should().BeNull();
        AppendCatalogueAuditReceipts(fixture, 1);
        var stale = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidDataException>();
    }

    private static SessionAttachmentQuery VersionQuery(SessionAttachmentSummary row, int endpoint = -1, bool remove = false)
    {
        long? Bound(int index, long value) => index != endpoint ? value : remove ? null : index % 2 == 0 ? value + 1 : long.MaxValue;
        return new(true, row.Session.Value.ToString("D"), SessionAttachmentFilter.Active, "text/", row.MediaType,
            AttachmentCopyStatus.Confirmed, row.StoredBytes, row.StoredBytes, row.File.Digest,
            row.ObservationSource.ToString("D"), row.Profile.Value.ToString("D"), row.CapturedAt, row.CapturedAt,
            Bound(0, row.SessionGeneration.Value), endpoint == 0 && !remove ? long.MaxValue : Bound(1, row.SessionGeneration.Value),
            Bound(2, row.CapturedGeneration.Value), endpoint == 2 && !remove ? long.MaxValue : Bound(3, row.CapturedGeneration.Value),
            Bound(4, row.StorageRevision.Value), endpoint == 4 && !remove ? long.MaxValue : Bound(5, row.StorageRevision.Value));
    }
}
