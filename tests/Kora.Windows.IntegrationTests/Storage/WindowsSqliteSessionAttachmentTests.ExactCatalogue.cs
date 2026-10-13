using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCatalogueValidatesNonmatchingSlotsAndFindsMatchBeyondFirstBoundedScanWithoutGlobalCap(bool includeRemoved)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        for (var index = 0; index < SessionAttachmentPage.MaximumScannedRows; index++)
        {
            if (index != 0) { await NewSession(fixture); }
            var attachment = await Attach(fixture, Encoding.UTF8.GetBytes("excluded retained slot"));
            var removal = await fixture.Store.PreviewRemoval(attachment.Session, fixture.Token);
            await NewControl(fixture, attachment.Session);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        }
        var target = new HostId<SessionIdentity>(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        await NewSession(fixture, target);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("PRIVATE_EXACT_BODY_NOT_SEARCHED"));
        var query = new SessionAttachmentQuery(includeRemoved, target.Value.ToString("D"));
        var clock = await fixture.Store.ReadRetentionAsync(target, fixture.Token);
        var tasks = fixture.Count("host_tasks");
        var audit = fixture.Count("security_audit_events");
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(SessionAttachmentPage.MaximumScannedRows);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        var last = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        var summary = last.Records.Should().ContainSingle().Which;
        summary.Session.Should().Be(target);
        summary.File.Should().Be(retained.File.Reference);
        last.ScannedRows.Should().Be(1);
        last.Next.Should().BeNull();
        fixture.Count("session_file").Should().Be(65);
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audit);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock);
        foreach (var changed in new[]
        {
            new SessionAttachmentQuery(includeRemoved),
            new SessionAttachmentQuery(includeRemoved, Guid.NewGuid().ToString("D")),
            new SessionAttachmentQuery(!includeRemoved, target.Value.ToString("D")),
        })
        {
            var wrong = () => fixture.Store.ReadAttachmentPage(changed, first.Next, () => true, fixture.Token).AsTask();
            await wrong.Should().ThrowAsync<InvalidDataException>();
        }
        using (var otherViewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            var wrong = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
            await wrong.Should().ThrowAsync<InvalidDataException>();
        }
        var removalReview = await fixture.Store.PreviewRemoval(target, fixture.Token);
        await NewControl(fixture, target);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removalReview, () => true, fixture.Token));
        var stale = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidDataException>();
        var staleSelection = () => fixture.Store.VerifyAttachmentSummary(summary, () => true, fixture.Token).AsTask();
        await staleSelection.Should().ThrowAsync<InvalidOperationException>();
        var fresh = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        var removed = await fixture.Store.ReadAttachmentPage(query, fresh.Next, () => true, fixture.Token);
        removed.Records.Length.Should().Be(includeRemoved ? 1 : 0);
        if (includeRemoved) { removed.Records.Single().CopyStatus.Should().Be(AttachmentCopyStatus.Unavailable); }
        // Even an excluded predecessor must be decoded and validated; exact ID never jumps over corruption.
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Format',99) WHERE session_id=(SELECT MIN(session_id) FROM session_file);");
        var corrupt = () => fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token).AsTask();
        await corrupt.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("query-null")]
    [InlineData("query-missing-id")]
    [InlineData("query-missing-flag")]
    [InlineData("query-unknown")]
    [InlineData("query-noncanonical")]
    [InlineData("signature")]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("expiry")]
    public async Task ExactCatalogueRejectsMalformedAuthenticatedQueryAndChangedSourceProfileOrExpiry(string boundary)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("private exact source"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = new SessionAttachmentQuery(false, fixture.Request.SessionId.Value.ToString("D"));
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        var cursor = first.Next!;
        if (boundary.StartsWith("query-", StringComparison.Ordinal))
        {
            var payload = JsonNode.Parse(Convert.FromBase64String(cursor.Split('.')[0]))!;
            switch (boundary)
            {
                case "query-null": payload["Query"] = null; break;
                case "query-missing-id": payload["Query"]!.AsObject().Remove("ExactSessionId"); break;
                case "query-missing-flag": payload["Query"]!.AsObject().Remove("IncludeRemoved"); break;
                case "query-unknown": payload["Query"]!["NameSubstring"] = "not admitted"; break;
                case "query-noncanonical": payload["Query"]!["ExactSessionId"] = query.ExactSessionId!.ToUpperInvariant(); break;
            }
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            var key = (byte[])typeof(Windows.Storage.WindowsSqliteHostInteractionStore)
                .GetField("attachmentCursorKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Store)!;
            cursor = Convert.ToBase64String(bytes) + "." + Convert.ToHexStringLower(HMACSHA256.HashData(key, bytes));
        }
        if (boundary is "signature") { cursor = cursor[..^1] + (cursor[^1] == '0' ? "1" : "0"); }
        if (boundary is "source") { fixture.Reopen(); await fixture.Store.InitializeAsync(fixture.Token); }
        if (boundary is "profile") { fixture.Mutate("UPDATE memory_profile SET profile_id='11111111-1111-1111-1111-111111111111';"); }
        if (boundary is "expiry") { fixture.Time.Now = fixture.Time.Now.AddMinutes(SessionAttachmentPage.SnapshotMinutes); }
        var read = () => fixture.Store.ReadAttachmentPage(query, cursor, () => true, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }
}
