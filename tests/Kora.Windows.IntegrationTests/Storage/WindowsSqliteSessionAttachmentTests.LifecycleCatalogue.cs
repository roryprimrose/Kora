using System.Text;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [WindowsFact]
    public async Task LifecycleCatalogueKeepsSessionRemovalDistinctFromSlotRemovalAndDefaultAllUnchanged()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var active = await Attach(fixture, Encoding.UTF8.GetBytes("active"));
        await NewSession(fixture);
        var activeRemoved = await Attach(fixture, Encoding.UTF8.GetBytes("active removed slot"));
        var removal = await fixture.Store.PreviewRemoval(activeRemoved.Session, fixture.Token);
        await NewControl(fixture, activeRemoved.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        await NewSession(fixture);
        var done = await Attach(fixture, Encoding.UTF8.GetBytes("done"));
        await NewControl(fixture, done.Session);
        await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token));
        await FinishControl(fixture);
        await NewSession(fixture);
        var removed = await Attach(fixture, Encoding.UTF8.GetBytes("removed session"));
        var disposition = await fixture.Store.PreviewDispositionAsync(removed.Session, new(1), 0, fixture.Token);
        await NewControl(fixture, removed.Session);
        await fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, disposition, () => true, fixture.Token));
        var audits = fixture.Count("security_audit_events");
        var tasks = fixture.Count("host_tasks");
        var clock = await fixture.Store.ReadRetentionAsync(done.Session, fixture.Token);
        foreach (var filter in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var includeRemoved in new[] { false, true })
        {
            var query = new SessionAttachmentQuery(includeRemoved, filter: filter);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.ScannedRows.Should().Be(4);
            page.Next.Should().BeNull();
            var expected = filter switch
            {
                SessionAttachmentFilter.All => includeRemoved
                    ? new[] { active.Session, activeRemoved.Session, done.Session, removed.Session } : [active.Session, done.Session],
                SessionAttachmentFilter.Active => includeRemoved ? [active.Session, activeRemoved.Session] : [active.Session],
                SessionAttachmentFilter.Done => [done.Session],
                SessionAttachmentFilter.Removed => includeRemoved ? [removed.Session] : [],
                _ => throw new InvalidOperationException()
            };
            page.Records.Select(row => row.Session).Should().BeEquivalentTo(expected);
            foreach (var row in page.Records)
            {
                var exact = await fixture.Store.ReadAttachmentPage(new(includeRemoved, row.Session.Value.ToString("D"), filter),
                    null, () => true, fixture.Token);
                exact.Records.Should().ContainSingle().Which.Should().Be(row);
            }
        }
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Count("host_tasks").Should().Be(tasks);
        (await fixture.Store.ReadRetentionAsync(done.Session, fixture.Token)).Should().Be(clock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifecycleCatalogueAdvancesEmptyFullScansAndValidatesIrrelevantCorruptSlots(bool includeRemoved)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        for (var index = 0; index < SessionAttachmentPage.MaximumScannedRows; index++)
        {
            if (index != 0) { await NewSession(fixture); }
            var attachment = await Attach(fixture, Encoding.UTF8.GetBytes("active excluded slot"));
            var removal = await fixture.Store.PreviewRemoval(attachment.Session, fixture.Token);
            await NewControl(fixture, attachment.Session);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        }
        var target = new HostId<SessionIdentity>(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        await NewSession(fixture, target);
        await Attach(fixture, Encoding.UTF8.GetBytes("done retained slot"));
        await NewControl(fixture, target);
        await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token));
        await FinishControl(fixture);
        var query = new SessionAttachmentQuery(includeRemoved, filter: SessionAttachmentFilter.Done, metadataText: "text/",
            exactMediaType: "text/markdown");
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(64);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        var last = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        last.Records.Should().ContainSingle().Which.Session.Should().Be(target);
        last.ScannedRows.Should().Be(1);
        last.Next.Should().BeNull();
        var changed = () => fixture.Store.ReadAttachmentPage(new(includeRemoved, filter: SessionAttachmentFilter.All),
            first.Next, () => true, fixture.Token).AsTask();
        await changed.Should().ThrowAsync<InvalidDataException>();
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Format',99) WHERE session_id=(SELECT MIN(session_id) FROM session_file);");
        var corrupt = () => fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token).AsTask();
        await corrupt.Should().ThrowAsync<InvalidDataException>();
        var corruptPredecessor = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token).AsTask();
        // Corrupt the fully scanned predecessor too: continuations must revalidate it before skipping ahead.
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Format',99) WHERE session_id=(SELECT MAX(session_id) FROM session_file WHERE session_id<'ffffffff-ffff-ffff-ffff-ffffffffffff');");
        await corruptPredecessor.Should().ThrowAsync<InvalidDataException>();
    }
}
