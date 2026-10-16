using System.Text;

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
    public async Task ExactFileIdsFilterCertifiedMetadataWithoutBodyReadsOrFormerIdReconstruction(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("SYNTHETIC_FILE_REFERENCE_BODY");
        var retained = await Attach(fixture, bytes);
        var original = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
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
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        checkpoint.Metadata = command =>
        {
            using var projection = command.ExecuteReader();
            projection.FieldCount.Should().Be(6);
            Enumerable.Range(0, projection.FieldCount).Select(projection.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("SourceId").And.NotContain("RevisionId").And.NotContain("ItemId").And.NotContain("LIKE");
        };
        var observed = (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Single();
        if (copy != AttachmentCopyStatus.Unavailable)
        { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        foreach (var file in new[] { original.File, observed.File })
        foreach (var query in new[] {
            new SessionAttachmentQuery(true, exactFileSourceId: file.SourceId.ToString("D")),
            new(true, exactFileRevisionId: file.RevisionId.ToString("D")),
            new(true, exactFileItemId: file.ItemId.ToString("D")),
            new(true, observed.Session.Value.ToString("D"), SessionAttachmentFilter.Active, "text/", observed.MediaType,
                copy, observed.StoredBytes, observed.StoredBytes, observed.File.Digest,
                observed.ObservationSource.ToString("D"), observed.Profile.Value.ToString("D"), observed.CapturedAt, observed.CapturedAt,
                observed.SessionGeneration.Value, observed.SessionGeneration.Value, observed.CapturedGeneration.Value, observed.CapturedGeneration.Value,
                observed.StorageRevision.Value, observed.StorageRevision.Value,
                file.SourceId.ToString("D"), file.RevisionId.ToString("D"), file.ItemId.ToString("D")) })
        {
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(1);
            page.Records.Length.Should().Be(copy == AttachmentCopyStatus.Confirmed ? 1 : 0);
            page.Next.Should().BeNull();
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference.ItemId','00000000-0000-0000-0000-000000000000');");
        var invalid = () => fixture.Store.ReadAttachmentPage(new(true, exactFileSourceId: Guid.NewGuid().ToString("D")),
            null, () => true, fixture.Token).AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task FileReferenceContinuationsBindEveryIdAndAllExistingPredicatesWithoutQueryDisclosure()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("synthetic file IDs"));
        var row = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        SessionAttachmentQuery Query(int change) => new(true,
            change == 7 ? null : row.Session.Value.ToString("D"), change == 8 ? SessionAttachmentFilter.All : SessionAttachmentFilter.Active,
            change == 9 ? null : "text/", change == 10 ? null : row.MediaType, change == 11 ? null : AttachmentCopyStatus.Confirmed,
            change == 12 ? null : 0, change == 13 ? null : 262144, change == 14 ? null : row.File.Digest,
            change == 15 ? null : row.ObservationSource.ToString("D"), change == 16 ? null : row.Profile.Value.ToString("D"),
            change == 17 ? null : row.CapturedAt, change == 18 ? null : row.CapturedAt,
            change == 19 ? null : row.SessionGeneration.Value, change == 20 ? null : row.SessionGeneration.Value,
            change == 21 ? null : row.CapturedGeneration.Value, change == 22 ? null : row.CapturedGeneration.Value,
            change == 23 ? null : row.StorageRevision.Value, change == 24 ? null : row.StorageRevision.Value,
            change == 1 ? null : change == 2 ? Guid.NewGuid().ToString("D") : row.File.SourceId.ToString("D"),
            change == 3 ? null : change == 4 ? Guid.NewGuid().ToString("D") : row.File.RevisionId.ToString("D"),
            change == 5 ? null : change == 6 ? Guid.NewGuid().ToString("D") : row.File.ItemId.ToString("D"));
        var query = Query(0);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        first.Records.Should().BeEmpty();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().Contain("CorrelationBinding").And.NotContain("ExactFileSourceId").And.NotContain("ExactFileRevisionId")
            .And.NotContain("ExactFileItemId").And.NotContain(row.File.SourceId.ToString("D"))
            .And.NotContain(row.File.RevisionId.ToString("D")).And.NotContain(row.File.ItemId.ToString("D"));
        foreach (var change in Enumerable.Range(1, 24))
        {
            var mismatch = () => fixture.Store.ReadAttachmentPage(Query(change), first.Next, () => true, fixture.Token).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        var flagMismatch = () => fixture.Store.ReadAttachmentPage(new(exactFileSourceId: row.File.SourceId.ToString("D")),
            first.Next, () => true, fixture.Token).AsTask();
        await flagMismatch.Should().ThrowAsync<InvalidDataException>();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancellation.Token).AsTask();
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
        }
        var denied = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => false, fixture.Token).AsTask();
        await denied.Should().ThrowAsync<InvalidOperationException>();
        var complete = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        complete.Records.Should().ContainSingle().Which.Should().Be(row);
        complete.Next.Should().BeNull();
        AppendCatalogueAuditReceipts(fixture, 1);
        var stale = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidDataException>();
    }
}
