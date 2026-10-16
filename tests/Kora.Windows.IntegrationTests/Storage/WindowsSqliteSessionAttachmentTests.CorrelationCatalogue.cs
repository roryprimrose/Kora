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
    public async Task CorrelationCatalogueUsesCertifiedMetadataWithoutBodyReadsOrTombstoneReconstruction(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("SYNTHETIC_CORRELATION_BODY");
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
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        checkpoint.Metadata = command =>
        {
            using var projection = command.ExecuteReader();
            projection.FieldCount.Should().Be(6);
            Enumerable.Range(0, projection.FieldCount).Select(projection.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("CapturedAt").And.NotContain("Profile").And.NotContain("LIKE");
        };
        var original = (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Single();
        if (copy != AttachmentCopyStatus.Unavailable)
        { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        foreach (var query in new[] {
            new SessionAttachmentQuery(true, exactObservationSource: original.ObservationSource.ToString("D")),
            new(true, exactDeviceProfile: original.Profile.Value.ToString("D")),
            new(true, capturedFromUtc: original.CapturedAt),
            new(true, capturedUntilUtc: original.CapturedAt),
            new(true, original.Session.Value.ToString("D"), SessionAttachmentFilter.Active, "text/", original.MediaType,
                copy, original.StoredBytes, original.StoredBytes, original.File.Digest,
                original.ObservationSource.ToString("D"), original.Profile.Value.ToString("D"), original.CapturedAt, original.CapturedAt) })
        {
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(1);
            page.Records.Length.Should().Be(copy == AttachmentCopyStatus.Confirmed ? 1 : 0);
            page.Next.Should().BeNull();
        }
        if (copy == AttachmentCopyStatus.Unavailable)
        {
            var json = JsonSerializer.Serialize(original);
            json.Should().NotContain(original.ObservationSource.ToString("D")).And.NotContain(original.Profile.Value.ToString("D"));
            original.RetainedCapturedAt.Should().BeNull();
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.CapturedAt','invalid');");
        var invalid = () => fixture.Store.ReadAttachmentPage(new(true, exactDeviceProfile: Guid.NewGuid().ToString("D")),
            null, () => true, fixture.Token).AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task CorrelationContinuationsBindEveryOptionalPredicateAndNeverSerializeQueryValues()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("synthetic correlation"));
        var row = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        SessionAttachmentQuery Query(int change) => new(true, row.Session.Value.ToString("D"), SessionAttachmentFilter.Active,
            "text/", row.MediaType, AttachmentCopyStatus.Confirmed, 0, 262144, row.File.Digest,
            change == 1 ? null : change == 2 ? Guid.NewGuid().ToString("D") : row.ObservationSource.ToString("D"),
            change == 3 ? null : change == 4 ? Guid.NewGuid().ToString("D") : row.Profile.Value.ToString("D"),
            change == 5 ? null : change == 6 ? row.CapturedAt.AddTicks(-1) : row.CapturedAt,
            change == 7 ? null : change == 8 ? row.CapturedAt.AddTicks(1) : row.CapturedAt);
        var query = Query(0);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        first.Records.Should().BeEmpty();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().Contain("CorrelationBinding").And.NotContain("ExactObservationSource").And.NotContain("ExactDeviceProfile")
            .And.NotContain("CapturedFromUtc").And.NotContain("CapturedUntilUtc");
        foreach (var change in Enumerable.Range(1, 8))
        {
            var mismatch = () => fixture.Store.ReadAttachmentPage(Query(change), first.Next, () => true, fixture.Token).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancellation.Token).AsTask();
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
        }
        var complete = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        complete.Records.Should().ContainSingle().Which.Should().Be(row);
        complete.Next.Should().BeNull();
    }
}
