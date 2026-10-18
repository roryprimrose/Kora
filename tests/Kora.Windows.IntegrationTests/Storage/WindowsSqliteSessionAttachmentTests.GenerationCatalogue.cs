using System.Text;
using System.Text.Json;

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
    public async Task GenerationRelationshipsUseOnlyConfirmedMetadataWithoutReadingBodies(AttachmentCopyStatus copy)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var bytes = Encoding.UTF8.GetBytes("synthetic generation body");
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
                var replacement = () => Replace(fixture, removal, bytes);
                await replacement.Should().ThrowAsync<IOException>();
            }
        }
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        checkpoint.Metadata = command =>
        {
            using var projection = command.ExecuteReader();
            projection.FieldCount.Should().Be(6);
            Enumerable.Range(0, projection.FieldCount).Select(projection.GetName).Should().NotContain("body");
            command.CommandText.Should().NotContain("CapturedGeneration").And.NotContain("SessionGeneration");
        };
        if (copy != AttachmentCopyStatus.Unavailable) { fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));"); }
        var original = (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Single();
        var tasks = fixture.Count("host_tasks");
        var audits = fixture.Count("security_audit_events");
        foreach (var relationship in Enum.GetValues<AttachmentGenerationRelationship>())
        {
            var query = new SessionAttachmentQuery(true, generationRelationship: relationship);
            var page = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            page.Records.Length.Should().Be(query.Matches(original) ? 1 : 0);
            page.ScannedRows.Should().Be(1);
            page.Next.Should().BeNull();
            if (copy == AttachmentCopyStatus.Unavailable)
            {
                var json = JsonSerializer.SerializeToNode(original)!;
                json["CapturedGeneration"].Should().BeNull();
                json["SessionGeneration"].Should().BeNull();
            }
        }
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Generation.Value',9223372036854775807);");
        var excluded = () => fixture.Store.ReadAttachmentPage(new(true,
            generationRelationship: AttachmentGenerationRelationship.EarlierObservedGeneration), null, () => true, fixture.Token).AsTask();
        await excluded.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task ObservedLifecycleGenerationChangeIsHistoricalRelationshipNotFreshnessOrNewAuthority()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("historical generation"));
        var same = new SessionAttachmentQuery(generationRelationship: AttachmentGenerationRelationship.SameObservedGeneration);
        var earlier = new SessionAttachmentQuery(generationRelationship: AttachmentGenerationRelationship.EarlierObservedGeneration);
        (await fixture.Store.ReadAttachmentPage(same, null, () => true, fixture.Token)).Records.Should().ContainSingle();
        (await fixture.Store.ReadAttachmentPage(earlier, null, () => true, fixture.Token)).Records.Should().BeEmpty();
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token));
        await FinishControl(fixture);
        (await fixture.Store.ReadAttachmentPage(same, null, () => true, fixture.Token)).Records.Should().BeEmpty();
        var page = await fixture.Store.ReadAttachmentPage(earlier, null, () => true, fixture.Token);
        page.Records.Should().ContainSingle().Which.CapturedGeneration.Should().Be(retained.Generation);
        page.Records.Single().SessionState.Should().Be(AttachmentSessionState.Done);
    }

    [Fact]
    public async Task GenerationContinuationsBindAllChoicesIncludingAnyDuringAuthorityValidation()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("generation continuation"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        foreach (var relationship in Enum.GetValues<AttachmentGenerationRelationship>())
        {
            var query = new SessionAttachmentQuery(generationRelationship: relationship);
            var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
            first.AuthorityValidationPending.Should().BeTrue();
            Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0])).Should().Contain("\"Version\":2");
            foreach (var other in Enum.GetValues<AttachmentGenerationRelationship>().Where(value => value != relationship))
            {
                var changed = () => fixture.Store.ReadAttachmentPage(new(generationRelationship: other),
                    first.Next, () => true, fixture.Token).AsTask();
                await changed.Should().ThrowAsync<InvalidDataException>();
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => true, cancellation.Token).AsTask();
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            var revoked = () => fixture.Store.ReadAttachmentPage(query, first.Next, () => false, fixture.Token).AsTask();
            await revoked.Should().ThrowAsync<InvalidOperationException>();
            var complete = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
            complete.Next.Should().BeNull();
            complete.Records.Length.Should().Be(relationship == AttachmentGenerationRelationship.EarlierObservedGeneration ? 0 : 1);
        }
    }

    [Fact]
    public async Task BoundedBatchValidatesProvenanceBeyondResultLimitWithoutSkippingUndisclosedSlots()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        for (var index = 0; index < 17; index++)
        {
            if (index != 0) { await NewSession(fixture); }
            var attachment = await Attach(fixture, Encoding.UTF8.GetBytes("removed generation slot"));
            var removal = await fixture.Store.PreviewRemoval(attachment.Session, fixture.Token);
            await NewControl(fixture, attachment.Session);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        }
        var query = new SessionAttachmentQuery(true);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.Records.Should().HaveCount(16);
        first.ScannedRows.Should().Be(17);
        var last = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        last.Records.Should().ContainSingle();
        last.Next.Should().BeNull();
        first.Records.Select(row => row.Session).Should().NotContain(last.Records.Single().Session);
        var changed = () => fixture.Store.ReadAttachmentPage(new(true,
            generationRelationship: AttachmentGenerationRelationship.SameObservedGeneration), first.Next, () => true, fixture.Token).AsTask();
        await changed.Should().ThrowAsync<InvalidDataException>();
        fixture.Mutate("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference.Digest','invalid') WHERE session_id=(SELECT MAX(session_id) FROM session_file);");
        foreach (var relationship in Enum.GetValues<AttachmentGenerationRelationship>())
        {
            var corrupt = () => fixture.Store.ReadAttachmentPage(new(true, generationRelationship: relationship),
                null, () => true, fixture.Token).AsTask();
            await corrupt.Should().ThrowAsync<InvalidDataException>();
        }
    }
}
