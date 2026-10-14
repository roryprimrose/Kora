using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed partial class WindowsSqliteExactGrantHistoryTests
{
    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task ActualCreatedConsumedAndSingleRevocationOutcomesAreExactImmutableCommitsAfterRestart(string scope)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync(scope);
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token));
        var consumed = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        await using var control = Control(f);
        await control.RevokeAsync(consumed, static () => true, f.Token);
        await control.RevokeAsync(consumed, static () => true, f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var page = await Read(f, exact);
        page.Records.Select(row => row.Audit.ActionId).Should().Equal(
            "approval.confirm", "approval.consume", "approval.revoke-exact", "approval.revoke-exact");
        page.Records[0].Provenance.GrantId.Should().Be(grant.Id);
        page.Records[0].Provenance.QuestionId.Should().NotBeNull();
        page.Records[1].Provenance.InteractionOutcome.Should().Be(HostInteractionOutcome.Consumed);
        foreach (var row in page.Records.Skip(2))
        {
            row.Provenance.RequestedAudit!.Outcome.Should().Be(SecurityAuditOutcome.Requested);
            row.Provenance.RequestedGrantRevision.Should().Be(consumed.Grant.Revision);
            row.Request.SessionId.Should().NotBe(f.Request.SessionId);
        }
        page.Records[^1].Audit.Outcome.Should().Be(SecurityAuditOutcome.Denied);
        page.Records[^1].Provenance.Changes.Should().BeEmpty();
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        var digests = page.Records.Select(row => row.Provenance.CommitDigest).ToArray();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        (await Read(f, exact)).Records.Select(row => row.Provenance.CommitDigest).Should().Equal(digests);
    }

    [WindowsFact]
    public async Task AtomicBatchMembershipIncludesEachExactRequestedAndCommittedMemberNotSameBindingUnrelatedGrants()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 3);
        var selection = new ExactGrantBatch(Guid.NewGuid(), (await f.Store.ReadExactGrantPageAsync(null, f.Token))
            .Records.Where(row => row.Grant.Id != grants[2].Id).ToImmutableArray());
        var preview = await f.Store.PreviewExactGrantBatchAsync(selection, f.Token);
        await using var control = Control(f);
        await control.RevokeBatchAsync(preview, static () => true, f.Token);
        foreach (var grant in grants)
        {
            var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
            var rows = (await Read(f, exact)).Records;
            if (grant == grants[2]) { rows.Should().ContainSingle(); continue; }
            var batch = rows.Single(row => string.Equals(row.Audit.ActionId, "approval.revoke-exact-batch", StringComparison.Ordinal));
            batch.Audit.ApprovalId.Should().BeNull();
            batch.Provenance.RequestedGrants.Should().ContainSingle().Which.Id.Should().Be(grant.Id.Value.ToString("D"));
            batch.Provenance.Changes.Should().ContainSingle().Which.Revision.Should().Be(2);
            batch.OtherChanges.Should().Be(1);
            batch.OtherRequestedGrants.Should().Be(1);
        }
    }

    [WindowsFact]
    public async Task StaleBatchIsRetainedRequestedReviewAndDeniedTerminalNeverCommittedMemberChanges()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var preview = await f.Store.PreviewExactGrantBatchAsync(
            new(Guid.NewGuid(), (await f.Store.ReadExactGrantPageAsync(null, f.Token)).Records), f.Token);
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grants[0].Id, grants[0].Revision, f.Token));
        await using var control = Control(f);
        await control.RevokeBatchAsync(preview, static () => true, f.Token);
        foreach (var grant in grants)
        {
            var row = (await Read(f, (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!)).Records.Last();
            row.Audit.ActionId.Should().Be("approval.revoke-exact-batch");
            row.Audit.Outcome.Should().Be(SecurityAuditOutcome.Denied);
            row.Provenance.RequestedGrants.Should().ContainSingle();
            row.Provenance.Changes.Should().BeEmpty();
            row.Provenance.InteractionOutcome.Should().Be(HostInteractionOutcome.Conflict);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContentAndSessionEndChangesResolveTypedExactMembersWithoutTopLevelApprovalId(bool content)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("session");
        if (content)
        {
            f.Proposal = new(f.Request, f.Proposal.ProposalId, f.Proposal.Revision,
                InteractionStorageFixture.Binding(changed: 0), f.Proposal.Effect, f.Proposal.ExpiresAt);
            await f.PublishAsync();
        }
        else { await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), active: false, remove: false, f.Token)); }
        var page = await Read(f, (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!);
        var changed = page.Records.Last();
        changed.Audit.ApprovalId.Should().BeNull();
        changed.Provenance.GrantId.Should().BeNull();
        changed.Provenance.Changes.Should().ContainSingle().Which.Id.Should().Be(grant.Id.Value.ToString("D"));
        changed.Audit.ActionId.Should().Be(content ? "authority.snapshot" : "session.done");
    }

    [WindowsFact]
    public async Task RetainedPerpetualHistoryDoesNotRequireRemovedOriginSessionOrInventOldAdmission()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        foreach (var task in (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token)).Records)
        {
            using var activity = HostActivity.BeginRoot(task.Request, HostActivityLayer.Application, HostOperation.Request);
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token);
        }
        var disposition = await f.Store.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.RunAsync(async () =>
        {
            await f.Store.RecordControlIntentAsync(f.Request, f.Token);
            await f.Store.DisposeSessionAsync(f.Request, disposition, static () => true, f.Token);
        });
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        exact.OriginSessionRemoved.Should().BeTrue();
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var page = await Read(f, exact, viewer);
        page.Records.Should().ContainSingle();
        page.Records[0].Request.SessionId.Should().Be(grant.ApprovedProposal.Request.SessionId);
        viewer.SessionId.Should().NotBe(grant.ApprovedProposal.Request.SessionId);
        f.Count("work_sessions").Should().Be(1);
    }

    [WindowsFact]
    public async Task ManyActualUsesRespectCompletePageBytesFiniteCeilingAndNoReadEffectsOrClockRenewal()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        for (var i = 0; i < 60; i++)
        {
            grant = (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token))).Grant!;
        }
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var clock = f.Time.Now;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var page = await Read(f, exact, viewer);
        page.Next.Should().NotBeNull();
        page.Records.Length.Should().BeLessThan(50, "complete serialized bytes including provenance, inspection and cursor govern the page");
        page.Records.Length.Should().BeLessThan((int)page.Current!.Grant.UseCount);
        page.Limitations.Should().Contain("not missing use rows").And.Contain("exhaustive retained history");
        var rows = new List<ExactGrantHistoryEntry>();
        var ceiling = page.AuditCeiling;
        do
        {
            ExactGrantHistoryPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(64 * 1024);
            page.Records.Length.Should().BeLessThanOrEqualTo(50);
            page.ScannedRows.Should().BeLessThanOrEqualTo(4096);
            page.AuditCeiling.Should().Be(ceiling);
            rows.AddRange(page.Records);
            if (page.Next is null) { break; }
            page = await Read(f, exact, viewer, page.Next);
        } while (true);
        rows.Should().HaveCount(61);
        rows.Select(row => row.Provenance.Sequence).Distinct().Should().HaveCount(61);
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        f.Time.Now.Should().Be(clock);
        // Appended authority is not pulled into an earlier immutable ceiling.
        var first = await Read(f, exact, viewer);
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token));
        (await Read(f, exact, viewer, first.Next)).AuditCeiling.Should().Be(first.AuditCeiling);
    }

    [WindowsFact]
    public async Task ZeroMatchScanBoundProgressesAndStoredCounterNeverFabricatesRetainedRows()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        // Fixture-only valid unrelated committed prefix, before the actual grant's creation.
        AppendUnrelated(f, 4200);
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var watch = Stopwatch.StartNew();
        var first = await Read(f, exact, viewer);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(4096);
        first.ScannedThrough.Should().Be(4096);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        var second = await Read(f, exact, viewer, first.Next);
        second.Records.Should().ContainSingle();
        second.ScannedThrough.Should().Be(second.AuditCeiling);
        second.Next.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ExactHistoryCursorRejectsCrossGrantViewerTimeTamperingMalformedAndStoreLifetime(int change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        AppendUnrelated(f, 4200);
        var exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var first = await Read(f, exact, viewer);
        var cursor = first.Next!;
        if (change == 0) { exact = (await f.Store.InspectExactGrantAsync(grants[1].Id, f.Token))!; }
        else if (change == 1) { viewer = HostRequest.Create(RequestOrigin.LocalUi); }
        else if (change == 2) { f.Time.Now = f.Time.Now.AddMinutes(15); }
        else if (change == 3) { cursor = "!" + cursor[1..]; }
        else if (change == 4) { cursor = "malformed"; }
        else { f.Reopen(); await f.Store.InitializeAsync(f.Token); exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!; }
        var read = () => Read(f, exact, viewer, cursor);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET hash=printf('%064d',1) WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.UnknownSchema',1) WHERE sequence=1;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=1;")]
    [InlineData("DELETE FROM authority_head;")]
    [InlineData("UPDATE perpetual_grants SET revision=99;")]
    [InlineData("PRAGMA user_version=999;")]
    public async Task CorruptSourceRowsHashesPrefixAndUnknownSchemaFailClosed(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Mutate(corruption);
        var read = () => Read(f, exact);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task MissingExactRecordIsUnavailableAndNeverUnfilteredHistoryFromOtherSameBindingGrants()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var exact = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        f.Mutate("DELETE FROM perpetual_grants WHERE approval_id='" + grants[0].Id.Value.ToString("D") + "';");
        var page = await Read(f, exact);
        page.Current.Should().BeNull();
        page.Records.Should().BeEmpty();
        page.Next.Should().BeNull();
    }

    [WindowsFact]
    public async Task ExpiredButPresentCommittedRowsRemainReadableWithoutReturningExpiredProposalAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        f.Time.Now = f.Time.Now.AddDays(91);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var page = await Read(f, exact);
        page.Records.Should().ContainSingle().Which.Retention.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        page.Current!.Grant.Should().Be(grant);
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingChangedCursorPredecessorAndMissingCurrentRecordNeverResetToUnfilteredHistory(int corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        AppendUnrelated(f, 4200);
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var first = await Read(f, exact, viewer);
        var sequence = first.ScannedThrough.ToString(CultureInfo.InvariantCulture);
        f.Mutate(corruption == 0 ? "DELETE FROM security_audit_events WHERE sequence=" + sequence + ";"
            : corruption == 1 ? "UPDATE security_audit_events SET envelope='{}' WHERE sequence=" + sequence + ";"
            : "DELETE FROM perpetual_grants;");
        var read = () => Read(f, exact, viewer, first.Next);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task OlderValidRetainedRowReplacementIsNotAdoptedAsCurrentAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        string payload;
        long sequence;
        using (var raw = f.OpenRaw())
        using (var query = raw.CreateCommand())
        {
            query.CommandText = "SELECT payload,audit_sequence FROM perpetual_grants;";
            using var row = query.ExecuteReader();
            row.Read().Should().BeTrue();
            payload = row.GetString(0);
            sequence = row.GetInt64(1);
        }
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token));
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        using (var raw = f.OpenRaw())
        using (var query = raw.CreateCommand())
        {
            query.CommandText = "UPDATE perpetual_grants SET payload=$payload,revision=1,audit_sequence=$sequence;";
            query.Parameters.AddWithValue("$payload", payload);
            query.Parameters.AddWithValue("$sequence", sequence);
            query.ExecuteNonQuery();
        }
        var read = () => Read(f, exact);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task ReplacedPrivateDatabaseAndMissingJournalAreUnavailableWithoutReplacementCreation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var copy = Path.Combine(Path.GetDirectoryName(f.DatabasePath)!, "retained-authority-copy.db");
        var directory = new RestrictedStorageDirectory(f.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
        using (var source = File.OpenRead(f.DatabasePath))
        using (var destination = directory.CreateNewFile(copy))
        {
            await source.CopyToAsync(destination, f.Token);
        }
        File.Delete(f.DatabasePath);
        File.Move(copy, f.DatabasePath);
        var read = () => Read(f, exact);
        await read.Should().ThrowAsync<InvalidDataException>();
        File.Delete(f.DatabasePath + "-journal");
        await read.Should().ThrowAsync<InvalidDataException>();
        File.Exists(f.DatabasePath + "-journal").Should().BeFalse();
    }

    [WindowsFact]
    public async Task CurrentOriginalViewerReadGateAndCancellationAreCheckedWithoutAdoptingGrantRequest()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var events = f.Count("security_audit_events");
        var noHost = () => f.Store.ReadExactGrantHistoryAsync(exact, viewer, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, null, static () => true, f.Token).AsTask();
        await noHost.Should().ThrowAsync<InvalidOperationException>();
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        var foreign = () => f.Store.ReadExactGrantHistoryAsync(exact, f.Request, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, null, static () => true, f.Token).AsTask();
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        var denied = () => f.Store.ReadExactGrantHistoryAsync(exact, viewer, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, null, static () => false, f.Token).AsTask();
        await denied.Should().ThrowAsync<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => f.Store.ReadExactGrantHistoryAsync(exact, viewer, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, null, static () => true, cancelled.Token).AsTask();
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        var calls = 0;
        var late = () => f.Store.ReadExactGrantHistoryAsync(exact, viewer, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, null, () => ++calls < 4, f.Token).AsTask();
        await late.Should().ThrowAsync<InvalidOperationException>();
        f.Count("security_audit_events").Should().Be(events);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NonOriginalNativeViewerCannotReadExactHistory(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var exact = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var read = () => Read(f, exact, HostRequest.Create(origin));
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task NativeRowsAreAccessibleScrollableReadOnlyAndHistoryRetiresExplicitSingleAndBatchReview()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await Grants(f, 2);
        await using var control = Control(f);
        await HeadlessSession.RunAsync(async () =>
        {
            using var state = new ExactGrantsViewModel(f.Store, control, new Access(), static () => true,
                NullLogger<ExactGrantsViewModel>.Instance);
            var window = new ExactGrantsWindow(state);
            try
            {
                window.Show();
                await state.RefreshAsync(f.Token);
                state.Selected = state.Records[0];
                await state.InspectAsync(f.Token);
                state.Reviewed = true;
                state.CanRevoke.Should().BeTrue();
                await state.ReadHistoryAsync(f.Token);
                state.HistoryRows.Should().ContainSingle();
                state.Reviewed.Should().BeFalse();
                state.CanRevoke.Should().BeFalse();
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Read-only committed exact grant history rows", StringComparison.Ordinal));
                list.Should().BeOfType<Kora.Controls.ScrollableListBox>();
                list.SelectedIndex = 0;
                state.CanRevoke.Should().BeFalse();
                state.BatchReviewed.Should().BeFalse();
                state.HistoryRows[0].Should().Contain("Committed exact grant change").And.Contain("not viewer authority");
                await state.InspectAsync(f.Token);
                state.Reviewed = true;
                state.CanRevoke.Should().BeTrue();
            }
            finally { window.Close(); }
            state.HistoryRows.Should().BeEmpty();
        });
    }

    private sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
    }

    private static async Task<ExactGrantHistoryPage> Read(InteractionStorageFixture f, ExactGrantInspection exact,
        HostRequest? viewer = null, string? cursor = null)
    {
        viewer ??= HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        return await f.Store.ReadExactGrantHistoryAsync(exact, viewer, ExactGrantHistoryQuery.Unfiltered, 1, viewer.RequestId.Value, cursor, static () => true, f.Token);
    }

    private static async Task<OperationGrant[]> Grants(InteractionStorageFixture f, int count)
    {
        var grants = new List<OperationGrant>();
        for (var index = 0; index < count; index++)
        {
            if (index > 0) { f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId); await f.AdmitAsync(newSession: false); }
            // Identical binding/digests deliberately do not establish membership.
            if (index > 0)
            {
                f.Proposal = new(f.Request, f.Proposal.ProposalId, f.Proposal.Revision,
                    grants[0].ApprovedProposal.Binding, f.Proposal.Effect, f.Proposal.ExpiresAt);
                await f.PublishAsync();
            }
            grants.Add(await f.GrantAsync("perpetual"));
        }
        return grants.ToArray();
    }

    private static ExactGrantControlAdmission Control(InteractionStorageFixture f) => new(f.Store, f.Store, new(f.Tasks), f.Authorization);

    private static void AppendUnrelated(InteractionStorageFixture f, int count)
    {
        using var raw = f.OpenRaw();
        using var transaction = raw.BeginTransaction();
        using var command = raw.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sequence,hash,envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
        long sequence;
        string hash;
        JsonObject envelope;
        using (var row = command.ExecuteReader())
        {
            row.Read().Should().BeTrue();
            sequence = row.GetInt64(0);
            hash = row.GetString(1);
            envelope = JsonNode.Parse(row.GetString(2))!.AsObject();
        }
        envelope["Changes"] = new JsonArray();
        envelope["ApprovalId"] = null;
        envelope["GrantRevision"] = null;
        envelope["QuestionId"] = null;
        envelope["QuestionRevision"] = null;
        envelope["RequestedAudit"] = null;
        envelope["RequestedGrantRevision"] = null;
        envelope["RequestedGrants"] = null;
        envelope["Audit"]!["ActionId"] = "authority.snapshot";
        envelope["Audit"]!["ApprovalId"] = null;
        for (var index = 0; index < count; index++)
        {
            var correlation = Guid.NewGuid().ToString("D");
            envelope["Audit"]!["CorrelationId"] = correlation;
            var json = envelope.ToJsonString();
            sequence++;
            var next = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                sequence.ToString(CultureInfo.InvariantCulture) + "\n" + hash + "\n" + json)));
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO security_audit_events VALUES($sequence,$correlation,$previous,$hash,$envelope);";
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$correlation", correlation);
            command.Parameters.AddWithValue("$previous", hash);
            command.Parameters.AddWithValue("$hash", next);
            command.Parameters.AddWithValue("$envelope", json);
            command.ExecuteNonQuery();
            hash = next;
        }
        command.Parameters.Clear();
        command.CommandText = "UPDATE authority_head SET sequence=$sequence,hash=$hash WHERE singleton=1;";
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$hash", hash);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
