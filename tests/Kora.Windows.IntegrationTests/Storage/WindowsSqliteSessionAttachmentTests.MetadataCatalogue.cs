using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [WindowsFact]
    public async Task LiteralCatalogueUsesOnlyExistingProjectionAndNeverSearchesBodyPathOrRemovedHeaders()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("PRIVATE_BODY_SEARCH_SENTINEL"));
        var clock = await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token);
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        checkpoint.Metadata = command =>
        {
            using var actual = command.ExecuteReader();
            actual.FieldCount.Should().Be(6);
            Enumerable.Range(0, actual.FieldCount).Select(actual.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("LIKE").And.NotContain("MetadataText");
        };
        // Equal-length body corruption must not affect passive stored metadata search.
        fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));");
        foreach (var text in new[] { retained.Session.Value.ToString("D"), retained.File.Reference.SourceId.ToString("D"),
            retained.File.Reference.RevisionId.ToString("D"), retained.File.Reference.ItemId.ToString("D"),
            retained.File.Reference.Digest, "text/" })
        {
            var query = new SessionAttachmentQuery(true, retained.Session.Value.ToString("D"), SessionAttachmentFilter.Active, text);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.Records.Should().ContainSingle().Which.File.Should().Be(retained.File.Reference);
            JsonSerializer.Serialize(page).Should().NotContain("MetadataText").And.NotContain("PRIVATE_BODY")
                .And.NotContain("Synthetic").And.NotContain("CanonicalPath");
        }
        foreach (var text in new[] { "PRIVATE_BODY_SEARCH_SENTINEL", "Synthetic", "*", "%", "TEXT/", " text/", " " })
        {
            var page = await fixture.Store.ReadAttachmentPage(new(true, metadataText: text), null, () => true, fixture.Token);
            page.Records.Should().BeEmpty();
            page.ScannedRows.Should().Be(1);
            page.Next.Should().BeNull();
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET body=X'" + Convert.ToHexString(Encoding.UTF8.GetBytes("PRIVATE_BODY_SEARCH_SENTINEL")) + "';");
        (await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token)).Should().Be(clock);
        var review = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, review, () => true, fixture.Token));
        (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Should().ContainSingle();
        foreach (var text in new[] { retained.Session.Value.ToString("D"), retained.File.Reference.Digest, "text/" })
        {
            (await fixture.Store.ReadAttachmentPage(new(true, metadataText: text), null, () => true, fixture.Token))
                .Records.Should().BeEmpty();
        }
    }

    [WindowsFact]
    public async Task LiteralCatalogueAuthorityContinuationBindsExactTextWithoutPuttingItInTokenAndPreservesOrder()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("private retained source"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = new SessionAttachmentQuery(true, metadataText: "text/");
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().NotContain("text/").And.NotContain("MetadataText");
        foreach (var text in new string?[] { null, "text", "TEXT/", " text/" })
        {
            var changed = () => fixture.Store.ReadAttachmentPage(new(true, metadataText: text), first.Next, () => true, fixture.Token).AsTask();
            await changed.Should().ThrowAsync<InvalidDataException>();
        }
        var second = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        second.Records.Should().ContainSingle();
        second.Next.Should().BeNull();
        (await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token)).Records.Should().Equal(second.Records);
    }
}
