using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    [InlineData(AttachmentCopyStatus.Confirmed)]
    [InlineData(AttachmentCopyStatus.Held)]
    [InlineData(AttachmentCopyStatus.Unavailable)]
    public async Task DigestCatalogueMatchesOnlyCertifiedCopiesWithoutReadingBodyOrReconstructingRemovedDigest(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("PRIVATE_DIGEST_BODY_SENTINEL");
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
            command.CommandText.Should().NotContain("LIKE").And.NotContain("Digest");
        };
        if (copy != AttachmentCopyStatus.Unavailable)
        { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        foreach (var digest in new string?[] { null, retained.File.Digest, new string('b', 64) })
        foreach (var includeRemoved in new[] { false, true })
        {
            var query = new SessionAttachmentQuery(includeRemoved, retained.Session.Value.ToString("D"),
                SessionAttachmentFilter.Active, exactDigest: digest);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(1);
            page.Records.Length.Should().Be((includeRemoved || copy != AttachmentCopyStatus.Unavailable)
                && (digest is null || copy == AttachmentCopyStatus.Confirmed && string.Equals(digest, retained.File.Digest, StringComparison.Ordinal)) ? 1 : 0);
            if (copy == AttachmentCopyStatus.Unavailable)
            { JsonSerializer.Serialize(page).Should().NotContain(retained.File.Digest); }
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference.Digest','" + new string('A', 64) + "');");
        var invalid = () => fixture.Store.ReadAttachmentPage(new(true, Guid.NewGuid().ToString("D"),
            SessionAttachmentFilter.Removed, exactDigest: new string('b', 64)), null, () => true, fixture.Token).AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DigestContinuationBindsKeyedFingerprintEveryFilterAuthorityViewerCancellationAndExpiry()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("private retained digest source"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = new SessionAttachmentQuery(true, retained.Session.Value.ToString("D"), SessionAttachmentFilter.Active,
            "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262144, retained.File.Digest);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        first.Records.Should().BeEmpty();
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0]));
        payload.Should().NotContain(retained.File.Digest).And.NotContain("ExactDigest").And.Contain("DigestBinding");
        foreach (var changed in new[]
        {
            new SessionAttachmentQuery(true, query.ExactSessionId, query.Filter, "text/", "text/markdown",
                AttachmentCopyStatus.Confirmed, 0, 262144),
            new(true, query.ExactSessionId, query.Filter, "text/", "text/markdown",
                AttachmentCopyStatus.Confirmed, 0, 262144, new string('b', 64)),
            new(false, query.ExactSessionId, query.Filter, "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262144, query.ExactDigest),
            new(true, null, query.Filter, "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, SessionAttachmentFilter.Done, "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, query.Filter, "text", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, query.Filter, "text/", "text/plain", AttachmentCopyStatus.Confirmed, 0, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, query.Filter, "text/", "text/markdown", AttachmentCopyStatus.Held, 0, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, query.Filter, "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 1, 262144, query.ExactDigest),
            new(true, query.ExactSessionId, query.Filter, "text/", "text/markdown", AttachmentCopyStatus.Confirmed, 0, 262143, query.ExactDigest)
        })
        {
            var mismatch = () => fixture.Store.ReadAttachmentPage(changed, first.Next, () => true, fixture.Token).AsTask();
            await mismatch.Should().ThrowAsync<InvalidDataException>();
        }
        foreach (var mutation in new[] { "missing", "changed" })
        {
            var node = JsonNode.Parse(payload)!;
            if (string.Equals(mutation, "missing", StringComparison.Ordinal)) { node.AsObject().Remove("DigestBinding"); }
            if (string.Equals(mutation, "changed", StringComparison.Ordinal)) { node["DigestBinding"] = new string('b', 64); }
            var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
            var key = (byte[])typeof(Windows.Storage.WindowsSqliteHostInteractionStore)
                .GetField("attachmentCursorKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Store)!;
            var cursor = Convert.ToBase64String(bytes) + "." + Convert.ToHexStringLower(HMACSHA256.HashData(key, bytes));
            var tampered = () => fixture.Store.ReadAttachmentPage(query, cursor, () => true, fixture.Token).AsTask();
            await tampered.Should().ThrowAsync<InvalidDataException>();
        }
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            var viewer = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
            await viewer.Should().ThrowAsync<InvalidDataException>();
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancelled.Token).AsTask();
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        var privateRead = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => false, fixture.Token).AsTask();
        await privateRead.Should().ThrowAsync<InvalidOperationException>();
        var last = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        var summary = last.Records.Should().ContainSingle().Which;
        last.Next.Should().BeNull();
        var removal = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        var stale = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidDataException>();
        var selection = () => fixture.Store.VerifyAttachmentSummary(summary, () => true, fixture.Token).AsTask();
        await selection.Should().ThrowAsync<InvalidOperationException>();
        var current = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(SessionAttachmentPage.SnapshotMinutes);
        var expired = () => fixture.Store.ReadAttachmentPage(query, current.Next, () => true, fixture.Token).AsTask();
        await expired.Should().ThrowAsync<InvalidDataException>();
    }
}
