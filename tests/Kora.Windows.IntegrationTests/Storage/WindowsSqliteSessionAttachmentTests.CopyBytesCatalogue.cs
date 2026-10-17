using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData(AttachmentCopyStatus.Confirmed)]
    [InlineData(AttachmentCopyStatus.Held)]
    [InlineData(AttachmentCopyStatus.Unavailable)]
    public async Task CopyAndByteCatalogueUsesValidatedRetainedMetadataNeverBodyOrTombstoneSentinels(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("PRIVATE_BODY_SENTINEL");
        var retained = await Attach(fixture, bytes);
        if (copy != AttachmentCopyStatus.Confirmed)
        {
            var removal = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
            await NewControl(fixture, retained.Session);
            if (copy == AttachmentCopyStatus.Unavailable)
            { await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token)); }
            else
            {
                fixture.Reopen(new AttachmentCheckpoint { Copies = () => throw new IOException("Held copy certification.") });
                await fixture.Store.InitializeAsync(fixture.Token);
                var replace = () => Replace(fixture, removal, bytes);
                await replace.Should().ThrowAsync<IOException>();
            }
        }
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        checkpoint.Metadata = command =>
        {
            using var projection = command.ExecuteReader();
            projection.FieldCount.Should().Be(6);
            Enumerable.Range(0, projection.FieldCount).Select(projection.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("StoredBytes").And.NotContain("CopyStatus").And.NotContain("LIKE");
        };
        if (copy != AttachmentCopyStatus.Unavailable)
        { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        foreach (var status in new AttachmentCopyStatus?[] { null, AttachmentCopyStatus.Confirmed, AttachmentCopyStatus.Held, AttachmentCopyStatus.Unavailable })
        foreach (var includeRemoved in new[] { false, true })
        foreach (var (minimum, maximum) in new (long?, long?)[]
        { (null, null), (0, null), (null, 0), (0, 0), (null, bytes.Length), (bytes.Length, null), (bytes.Length, bytes.Length),
            (bytes.Length + 1, null), (null, bytes.Length - 1), (0, LocalFilePolicy.MaximumBytes) })
        {
            var query = new SessionAttachmentQuery(includeRemoved, retained.Session.Value.ToString("D"),
                SessionAttachmentFilter.Active, copyStatus: status, minimumStoredBytes: minimum, maximumStoredBytes: maximum);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            var expected = (includeRemoved || copy != AttachmentCopyStatus.Unavailable) && (status is null || status == copy)
                && (minimum is null && maximum is null || copy != AttachmentCopyStatus.Unavailable
                    && (minimum is null || bytes.Length >= minimum) && (maximum is null || bytes.Length <= maximum));
            page.Records.Length.Should().Be(expected ? 1 : 0);
            page.ScannedRows.Should().Be(1);
            page.Next.Should().BeNull();
            if (expected)
            {
                page.Records[0].CopyStatus.Should().Be(copy);
                page.Records[0].RetainedStoredBytes.Should().Be(copy == AttachmentCopyStatus.Unavailable ? null : bytes.Length);
            }
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
    }

    [Theory]
    [InlineData("UPDATE session_file SET revision=revision+1;")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.MediaType','text/html');")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Metadata.ByteLength',-1);")]
    [InlineData("UPDATE work_sessions SET state=1;")]
    [InlineData("UPDATE security_audit_events SET hash=printf('%064d',0) WHERE sequence=1;")]
    public async Task CopyAndBytesNeverExcludeCorruptIrrelevantRowsOrAuthority(string mutation)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("retained source"));
        fixture.Mutate(mutation);
        var query = new SessionAttachmentQuery(true, Guid.NewGuid().ToString("D"), SessionAttachmentFilter.Removed,
            "never matches", "text/plain", AttachmentCopyStatus.Unavailable, 0, 0);
        var read = () => fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }
}
