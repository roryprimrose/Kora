using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteExactGrantHistoryTests
{
    [WindowsFact]
    public async Task TypedOutcomeReasonAndCombinedFiltersReadOnlyAuthoritativeExactMembersNotRequestedReview()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grants = await Grants(f, 2);
        var inspected = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        await using var control = Control(f);
        await control.RevokeAsync(inspected, static () => true, f.Token);
        await control.RevokeAsync(inspected, static () => true, f.Token);
        inspected = (await f.Store.InspectExactGrantAsync(grants[0].Id, f.Token))!;
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var all = await ReadFiltered(f, inspected, ExactGrantHistoryQuery.Unfiltered);
        all.Records.Should().HaveCount(3);
        all.NonmatchingRows.Should().Be(all.ScannedRows - 3);
        var denied = all.Records.Single(row => row.Audit.Outcome == SecurityAuditOutcome.Denied);
        denied.Provenance.RequestedAudit!.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        denied.Audit.ReasonCode.Should().NotBeNull();
        foreach (var query in new[]
        {
            new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied),
            new ExactGrantHistoryQuery(reasonCode: denied.Audit.ReasonCode),
            new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied, denied.Audit.ReasonCode),
        })
        {
            var page = await ReadFiltered(f, inspected, query);
            page.Records.Should().ContainSingle().Which.Audit.CorrelationId.Should().Be(denied.Audit.CorrelationId);
            page.NonmatchingRows.Should().Be(page.ScannedRows - 1);
            page.AuditCeiling.Should().Be(all.AuditCeiling);
            page.AuditCeilingDigest.Should().Be(all.AuditCeilingDigest);
            page.Current.Should().Be(inspected);
        }
        (await ReadFiltered(f, inspected, new(SecurityAuditOutcome.Requested))).Records.Should().BeEmpty();
        (await ReadFiltered(f, inspected, new(SecurityAuditOutcome.Succeeded, denied.Audit.ReasonCode))).Records.Should().BeEmpty();
        (await ReadFiltered(f, inspected, new(reasonCode: "unknown-exact-reason"))).Records.Should().BeEmpty();
        foreach (var initiator in Enum.GetValues<SecurityAuditInitiator>())
        {
            foreach (var outcome in Enum.GetValues<SecurityAuditOutcome>())
            {
                foreach (var reason in new[] { null, denied.Audit.ReasonCode, "unknown-exact-reason" })
                {
                    var query = new ExactGrantHistoryQuery(outcome, reason, initiator);
                    var page = await ReadFiltered(f, inspected, query);
                    page.Records.Select(row => row.Audit.CorrelationId).Should().Equal(all.Records
                        .Where(row => row.Audit.Initiator == initiator && row.Audit.Outcome == outcome
                            && (reason is null || string.Equals(row.Audit.ReasonCode, reason, StringComparison.Ordinal)))
                        .Select(row => row.Audit.CorrelationId));
                    page.NonmatchingRows.Should().Be(page.ScannedRows - page.Records.Length);
                    page.Current.Should().Be(inspected);
                }
            }
            var onlyInitiator = await ReadFiltered(f, inspected, new(initiator: initiator));
            onlyInitiator.Records.Select(row => row.Audit.CorrelationId).Should().Equal(all.Records
                .Where(row => row.Audit.Initiator == initiator).Select(row => row.Audit.CorrelationId));
        }
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
        Activity.Current.Should().BeNull();
    }

    [WindowsFact]
    public async Task FilteredZeroMatchPrefixAdvancesWithoutExhaustionAndRetainsExpiredMatchingRows()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        AppendUnrelated(f, 4200);
        var grant = await f.GrantAsync("perpetual");
        var inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Time.Now = f.Time.Now.AddDays(91);
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var query = new ExactGrantHistoryQuery(SecurityAuditOutcome.Succeeded, initiator: SecurityAuditInitiator.TypedCommand);
        var first = await ReadFiltered(f, inspected, query, viewer);
        first.Records.Should().BeEmpty();
        first.ScannedRows.Should().Be(4096);
        first.NonmatchingRows.Should().Be(4096);
        first.ScannedThrough.Should().Be(4096);
        first.ScanLimitReached.Should().BeTrue();
        first.Next.Should().NotBeNull();
        var second = await ReadFiltered(f, inspected, query, viewer, first.Next);
        second.Records.Should().ContainSingle().Which.Retention.Should().Be(Kora.Core.Diagnostics.EvidenceSegmentStatus.ExpiredButPresent);
        second.Next.Should().BeNull();
        second.ScannedThrough.Should().Be(first.AuditCeiling);
        second.AuditCeilingDigest.Should().Be(first.AuditCeilingDigest);
        second.ScannedRows.Should().BeLessThanOrEqualTo(4096);
        ExactGrantHistoryPage.Serialize(second).Length.Should().BeLessThanOrEqualTo(65536);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task AuthenticatedContinuationRejectsChangedFilterTupleExactInspectionViewerAdmissionControlCeilingAndDigest(int change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        AppendUnrelated(f, 4100);
        var inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var query = new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied, "exact-private-query", SecurityAuditInitiator.VoiceCommand);
        var admission = Guid.NewGuid();
        var revision = 1L;
        var first = await ReadFiltered(f, inspected, query, viewer, admission: admission, revision: revision);
        first.Next.Should().NotBeNull();
        Encoding.UTF8.GetString(Convert.FromBase64String(first.Next!.Split('.')[0])).Should().NotContain("exact-private-query");
        if (change == 0) { query = new(SecurityAuditOutcome.Succeeded, "exact-private-query", SecurityAuditInitiator.VoiceCommand); }
        else if (change == 1) { query = new(SecurityAuditOutcome.Denied, "other", SecurityAuditInitiator.VoiceCommand); }
        else if (change == 2) { query = ExactGrantHistoryQuery.Unfiltered; }
        else if (change == 3) { viewer = new(new(Guid.NewGuid()), viewer.SessionId, viewer.TaskId, viewer.Origin, viewer.InvocationId); }
        else if (change == 4) { admission = Guid.NewGuid(); }
        else if (change == 5) { revision++; }
        else if (change == 6) { inspected = inspected with { Grant = inspected.Grant with { Revision = new(2) } }; }
        else if (change == 7) { f.Mutate("DELETE FROM security_audit_events WHERE sequence=" + first.AuditCeiling + ";"); }
        else if (change == 8) { f.Mutate("UPDATE security_audit_events SET hash=printf('%064d',1) WHERE sequence=" + first.AuditCeiling + ";"); }
        else if (change == 9) { f.Mutate("UPDATE authority_head SET sequence=0,hash=printf('%064d',0);"); }
        else if (change == 10) { f.Time.Now = f.Time.Now.AddMinutes(15); }
        else if (change == 11) { query = new(SecurityAuditOutcome.Denied, "exact-private-query", SecurityAuditInitiator.LocalUser); }
        else
        {
            query = new(SecurityAuditOutcome.Denied, "exact-private-query", SecurityAuditInitiator.VoiceCommand,
                retention: change == 12 ? EvidenceSegmentStatus.Present : null,
                committedFromUtc: change == 13 ? f.Time.Now : null, committedUntilUtc: change == 14 ? f.Time.Now : null,
                dueFromUtc: change == 15 ? f.Time.Now : null, dueUntilUtc: change == 16 ? f.Time.Now : null);
        }
        var read = () => ReadFiltered(f, inspected, query, viewer, first.Next, admission, revision);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task UnknownControlOrAdmissionNeverFallsBackToUnboundRead(long revision, bool admitted)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var read = () => ReadFiltered(f, inspected, ExactGrantHistoryQuery.Unfiltered,
            admission: admitted ? Guid.NewGuid() : Guid.Empty, revision: revision);
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task NativeHistoryFilterControlsAreFocusableNamedPassiveAndRequireFreshExplicitRead()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await f.GrantAsync("perpetual");
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
                await state.ReadHistoryAsync(f.Token);
                var outcome = window.GetVisualDescendants().OfType<ComboBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history committed event outcome", StringComparison.Ordinal) == true);
                var initiator = window.GetVisualDescendants().OfType<ComboBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history committed event initiator", StringComparison.Ordinal) == true);
                var reason = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Exact grant history canonical", StringComparison.Ordinal) == true);
                outcome.Focusable.Should().BeTrue();
                initiator.Focusable.Should().BeTrue();
                initiator.Items.Cast<ExactGrantHistoryInitiatorOption>().Should().Equal(ExactGrantHistoryQuery.InitiatorOptions);
                reason.Focusable.Should().BeTrue();
                outcome.Focus();
                outcome.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                initiator.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                reason.IsFocused.Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                initiator.IsFocused.Should().BeTrue();
                reason.Focus();
                reason.IsFocused.Should().BeTrue();
                var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
                initiator.SelectedIndex = 5;
                state.HistoryInitiatorFilter.Should().Be("ModelSuggestion");
                state.HistoryRows.Should().BeEmpty();
                state.CanReadHistory.Should().BeTrue();
                state.HistorySummary.Should().Contain("typing performs no read");
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().BeEmpty();
                foreach (var option in ExactGrantHistoryQuery.InitiatorOptions)
                {
                    initiator.SelectedItem = option;
                    state.HistoryInitiatorFilter.Should().Be(option.Label);
                    state.HistoryRows.Should().BeEmpty();
                }
                reason.Text = "not-retained";
                state.HistoryReasonCodeFilter.Should().Be("not-retained");
                state.HistoryRows.Should().BeEmpty();
                state.Records.Should().BeEmpty();
                state.Selected.Should().BeNull();
                state.CanRevoke.Should().BeFalse();
                state.CanReadHistory.Should().BeTrue();
                state.HistorySummary.Should().Contain("typing performs no read");
                await state.ReadHistoryCommand.ExecuteAsync();
                state.HistoryRows.Should().BeEmpty();
                state.HistorySummary.Should().Contain("none in this snapshot").And.Contain("nonmatching scanned rows");
                SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
                await state.ClearHistoryFiltersCommand.ExecuteAsync();
                state.HistoryOutcomeFilter.Should().Be("Any");
                state.HistoryInitiatorFilter.Should().Be("Any");
                ((ExactGrantHistoryInitiatorOption)initiator.SelectedItem!).Value.Should().BeNull();
                await state.ReadHistoryAsync(f.Token);
                state.HistoryRows.Should().ContainSingle();
                state.CanRevoke.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    private static async Task<ExactGrantHistoryPage> ReadFiltered(InteractionStorageFixture f, ExactGrantInspection exact,
        ExactGrantHistoryQuery query, HostRequest? viewer = null, string? cursor = null, Guid? admission = null, long revision = 1)
    {
        viewer ??= HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        return await f.Store.ReadExactGrantHistoryAsync(exact, viewer, query, revision, admission ?? viewer.RequestId.Value,
            cursor, static () => true, f.Token);
    }

    [WindowsFact]
    public async Task FiveSecondDeadlineCancelsFilteredScanDeterministicallyAndDisposesTimerWithoutWrites()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var clock = new DeadlineClock(f.Time.Now);
        var store = new WindowsSqliteHostInteractionStore(f.Paths, f.Tasks, clock);
        await store.InitializeAsync(f.Token);
        var inspected = (await store.InspectExactGrantAsync(grant.Id, f.Token))!;
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var before = SHA256.HashData(File.ReadAllBytes(f.DatabasePath));
        var calls = 0;
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        var read = () => store.ReadExactGrantHistoryAsync(inspected, viewer, new(SecurityAuditOutcome.Denied),
            1, Guid.NewGuid(), null, () =>
            {
                if (++calls == 3) { clock.Timer!.Fire(); }
                return true;
            }, f.Token).AsTask();
        await read.Should().ThrowAsync<OperationCanceledException>();
        clock.DueTime.Should().Be(TimeSpan.FromSeconds(5));
        clock.Timer!.Disposed.Should().BeTrue();
        SHA256.HashData(File.ReadAllBytes(f.DatabasePath)).Should().Equal(before);
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET envelope='{}' WHERE sequence=1;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET hash=printf('%064d',1) WHERE sequence=1;")]
    public async Task ExcludedRowsStillFailAuthoritativePrefixValidationRatherThanReturningEmptyMatches(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        var inspected = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!;
        f.Mutate(corruption);
        var read = () => ReadFiltered(f, inspected, new(SecurityAuditOutcome.Denied, "not-present", SecurityAuditInitiator.ModelSuggestion,
            EvidenceSegmentStatus.ExpiredButPresent, f.Time.Now, f.Time.Now, f.Time.Now, f.Time.Now));
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class DeadlineClock(DateTimeOffset now) : TimeProvider
    {
        internal DeadlineTimer? Timer { get; private set; }
        internal TimeSpan DueTime { get; private set; }
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            period.Should().Be(Timeout.InfiniteTimeSpan);
            return Timer = new(callback, state);
        }
    }

    private sealed class DeadlineTimer(TimerCallback callback, object? state) : ITimer
    {
        internal bool Disposed { get; private set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
