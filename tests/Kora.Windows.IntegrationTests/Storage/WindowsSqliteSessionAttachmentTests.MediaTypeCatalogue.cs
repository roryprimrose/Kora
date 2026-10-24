using System.Text;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData("text/plain", "C:\\Synthetic\\source.txt")]
    [InlineData("text/markdown", "C:\\Synthetic\\source.md")]
    [InlineData("text/markdown", "C:\\Synthetic\\source.markdown")]
    public async Task MediaCatalogueComposesEveryFilterOverValidatedMetadataAndTombstonesMatchOnlyAll(string type, string path)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("PRIVATE_BODY_SENTINEL"), path: path);
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        var clock = await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token);
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        checkpoint.Metadata = command => command.CommandText.Should().NotContain("LIKE").And.NotContain("MediaType");
        fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));");
        foreach (var mediaType in new string?[] { null, "text/plain", "text/markdown" })
        foreach (var filter in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var includeRemoved in new[] { false, true })
        foreach (var exact in new[] { false, true })
        {
            var query = new SessionAttachmentQuery(includeRemoved, exact ? retained.Session.Value.ToString("D") : null,
                filter, mediaType);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(1);
            page.Next.Should().BeNull();
            page.Records.Length.Should().Be((mediaType is null || string.Equals(mediaType, type, StringComparison.Ordinal))
                && filter is SessionAttachmentFilter.All or SessionAttachmentFilter.Active ? 1 : 0);
            if (!page.Records.IsEmpty) { page.Records[0].MediaType.Should().Be(type); }
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET body=X'" + Convert.ToHexString(Encoding.UTF8.GetBytes("PRIVATE_BODY_SENTINEL")) + "';");
        (await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token)).Should().Be(clock);
        var removal = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Should().ContainSingle();
        foreach (var mediaType in new[] { "text/plain", "text/markdown" })
        {
            (await fixture.Store.ReadAttachmentPage(new(true, exactMediaType: mediaType), null, () => true, fixture.Token))
                .Records.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    public async Task MediaContinuationBindsAllQueryFieldsDigestViewerSourcePrivateAndCompleteAuthorityPrefix(string type)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("private source"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = new SessionAttachmentQuery(true, retained.Session.Value.ToString("D"), SessionAttachmentFilter.Active, type,
            AttachmentCopyStatus.Confirmed, 0, 262144);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        first.Records.Should().BeEmpty();
        foreach (var changed in new[]
        {
            new SessionAttachmentQuery(true, query.ExactSessionId, query.Filter, type, null, 0, 262144),
            new(true, query.ExactSessionId, query.Filter, type, AttachmentCopyStatus.Held, 0, 262144),
            new(true, query.ExactSessionId, query.Filter, type, query.CopyStatus, null, 262144),
            new(true, query.ExactSessionId, query.Filter, type, query.CopyStatus, 1, 262144),
            new(true, query.ExactSessionId, query.Filter, type, query.CopyStatus, 0, null),
            new(true, query.ExactSessionId, query.Filter, type, query.CopyStatus, 0, 262143),
            new SessionAttachmentQuery(true, query.ExactSessionId, query.Filter),
            new(true, query.ExactSessionId, query.Filter,
                string.Equals(type, "text/plain", StringComparison.Ordinal) ? "text/markdown" : "text/plain"),
            new(false, query.ExactSessionId, query.Filter, type),
            new(true, null, query.Filter, type),
            new(true, query.ExactSessionId, SessionAttachmentFilter.All, type),
        })
        {
            var mismatch = () => fixture.Store.ReadAttachmentPage(changed, first.Next, () => true, fixture.Token).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        var tampered = first.Next![..^1] + (first.Next[^1] == 'a' ? "b" : "a");
        var tamper = () => fixture.Store.ReadAttachmentPage(query, tampered, () => true, fixture.Token).AsTask();
        await tamper.Should().ThrowAsync<InvalidDataException>();
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            var viewer = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
            await viewer.Should().ThrowAsync<InvalidDataException>();
        }
        var privateRead = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => false, fixture.Token).AsTask();
        await privateRead.Should().ThrowAsync<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancelled.Token).AsTask();
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        var last = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        last.Records.Length.Should().Be(string.Equals(type, "text/markdown", StringComparison.Ordinal) ? 1 : 0);
        last.Next.Should().BeNull();
        AppendCatalogueAuditReceipts(fixture, 1);
        var changedHead = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        await changedHead.Should().ThrowAsync<InvalidDataException>();
        var current = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(SessionAttachmentPage.SnapshotMinutes);
        var expired = () => fixture.Store.ReadAttachmentPage(query, current.Next, () => true, fixture.Token).AsTask();
        await expired.Should().ThrowAsync<InvalidDataException>();
        current = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var source = () => fixture.Store.ReadAttachmentPage(query, current.Next, () => true, fixture.Token).AsTask();
        await source.Should().ThrowAsync<InvalidDataException>();
        current = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        fixture.Mutate("UPDATE security_audit_events SET envelope=json_set(envelope,'$.SessionGeneration.Value',99) WHERE sequence=(SELECT MAX(sequence) FROM security_audit_events)-1;");
        var corruptPrefix = () => fixture.Store.ReadAttachmentPage(query, current.Next, () => true, fixture.Token).AsTask();
        await corruptPrefix.Should().ThrowAsync<InvalidDataException>();
    }
}
