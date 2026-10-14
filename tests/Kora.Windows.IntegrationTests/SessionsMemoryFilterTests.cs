using System.Collections.Immutable;
using System.Diagnostics;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsMemoryFilterTests
{
    [WindowsFact]
    public async Task EveryTypedStateAndCombinedExactGuidFilterUsesOnlyTheFrozenContentFreeSnapshot()
    {
        await using var f = await Fixture.Create();
        f.Model.CanFilterMemories.Should().BeFalse();
        f.Model.MemoryIdFilter = "not admitted before List";
        f.Model.MemoryIdFilter.Should().BeEmpty();
        foreach (var state in new[] { "proposed", "reviewed", "rejected", "enabled", "disabled", "forgotten" })
        {
            await f.CreateMemory(state);
        }
        await f.Model.ListMemoriesAsync();
        var snapshot = f.Model.MemoryRecords.ToArray();
        snapshot.Should().HaveCount(6);
        MemoryReviewState?[] reviews = [null, .. Enum.GetValues<MemoryReviewState>().Select(value => (MemoryReviewState?)value)];
        MemoryRetentionState?[] retentions = [null, .. Enum.GetValues<MemoryRetentionState>().Select(value => (MemoryRetentionState?)value)];
        f.Model.MemoryReviewFilters.Select(option => option.Value).Should()
            .Equal(reviews);
        f.Model.MemoryRetentionFilters.Select(option => option.Value).Should()
            .Equal(retentions);
        f.Model.MemoryReviewFilters[0].ToString().Should().Be("Any");
        f.Model.MemoryRetentionFilters[0].ToString().Should().Be("Any");
        var calls = f.Store.Calls;
        var activities = f.Activities;
        var logs = f.Logger.Messages.ToArray();
        var bytes = File.ReadAllBytes(f.Storage.DatabasePath);
        var clock = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Storage.Token);
        foreach (var review in reviews)
        {
            foreach (var retention in retentions)
            {
                f.Model.MemoryReviewFilter = review;
                f.Model.MemoryRetentionFilter = retention;
                f.Model.MemoryRecords.Should().Equal(snapshot.Where(row =>
                    (review is null || row.Review == review) && (retention is null || row.Retention == retention)));
                foreach (var row in snapshot)
                {
                    f.Model.MemoryIdFilter = row.Id.ToString("D").ToUpperInvariant();
                    f.Model.MemoryRecords.Should().Equal(snapshot.Where(candidate =>
                        candidate.Id == row.Id && (review is null || candidate.Review == review)
                        && (retention is null || candidate.Retention == retention)));
                }
                f.Model.MemoryIdFilter = string.Empty;
            }
        }
        f.Model.ClearMemoryFilters();
        f.Model.MemoryIdFilter = "abcdefab-1234-4567-89ab-abcdefabcdef";
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().StartWith("0 of 6");
        foreach (var input in new[] { "abc", " ", Guid.Empty.ToString("D"), snapshot[0].Id.ToString("N"),
            snapshot[0].Id.ToString("B"), " " + snapshot[0].Id.ToString("D"), snapshot[0].Id.ToString("D") + "\n" })
        {
            f.Model.MemoryIdFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("canonical D");
        }
        f.Model.ClearMemoryFilters();
        f.Model.MemoryReviewFilter = (MemoryReviewState)99;
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().Contain("Unknown");
        f.Model.ClearMemoryFilters();
        f.Model.MemoryRetentionFilter = (MemoryRetentionState)(-1);
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().Contain("Unknown");
        f.Model.ClearMemoryFilters();
        f.Model.MemoryRecords.Should().Equal(snapshot);
        f.Store.Calls.Should().Be(calls, "typing and changing filters perform no storage or admission");
        f.Activities.Should().Be(activities, "filter edits create no host operations or model-context effects");
        f.Logger.Messages.Should().Equal(logs, "query text is never logged");
        File.ReadAllBytes(f.Storage.DatabasePath).Should().Equal(bytes, "filters persist nothing");
        (await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Storage.Token)).Should().Be(clock);
        f.Model.MemoryDetail.Should().BeEmpty();
        f.Model.MemoryDraft.Should().BeEmpty();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("review")]
    [InlineData("retention")]
    [InlineData("clear")]
    public async Task FilterEditsRetireSelectionInspectionReplacementAndEveryMutationEligibility(string field)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.SelectMemory(row);
        f.Model.CanEditMemory.Should().BeFalse("selection alone does not inspect");
        await f.Model.InspectMemoryAsync();
        f.Model.CanDisableMemory.Should().BeTrue();
        f.Model.CanEditMemory.Should().BeTrue();
        f.Model.MemoryDraft = "unsubmitted replacement";
        var calls = f.Store.Calls;
        switch (field)
        {
            case "id": f.Model.MemoryIdFilter = row.Id.ToString("D"); break;
            case "review": f.Model.MemoryReviewFilter = row.Review; break;
            case "retention": f.Model.MemoryRetentionFilter = row.Retention; break;
            case "clear": f.Model.ClearMemoryFilters(); break;
        }
        AssertRetired(f.Model);
        f.Model.MemoryRecords.Should().Equal([row], "even an unchanged visible exact identity cannot carry selection forward");
        f.Model.SelectMemory(row with { Revision = row.Revision + 1 });
        f.Model.SelectedMemory.Should().BeNull("a foreign or stale row is not a visible result");
        f.Model.SelectMemory(row);
        f.Model.CanInspectMemory.Should().BeTrue();
        f.Model.CanEditMemory.Should().BeFalse();
        f.Model.CanDisableMemory.Should().BeFalse();
        f.Store.Calls.Should().Be(calls);
        var review = () => f.Management.ExecuteAsync(new(MemoryCommandOperation.Review,
            f.Storage.Request.SessionId.Value, row.Id, row.Revision, true), RequestOrigin.LocalUi, () => true, f.Storage.Token);
        await review.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Inspect*");
        await f.Model.InspectMemoryAsync();
        f.Model.MemoryDraft.Should().Be("private exact value");
        f.Model.MemoryClass.Should().Be(MemoryContentClass.Decision);
        f.Model.CanEditMemory.Should().BeTrue();
        f.Model.CanDisableMemory.Should().BeTrue();
    }

    [WindowsFact]
    public async Task ExplicitListRefreshAndProposalReviewEditDisableForgetReplaceRowsWithoutContentDisclosureOrSelectionCarry()
    {
        await using var f = await Fixture.Create();
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryReviewFilter = MemoryReviewState.Proposed;
        f.Model.MemoryDraft = " exact e\u0301 value ";
        f.Model.MemoryClass = MemoryContentClass.ResponsePreference;
        await f.Model.ProposeMemoryAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.SelectedMemory.Should().BeNull();
        f.Model.MemoryDetail.Should().NotContain("exact e");
        f.Model.SelectMemory(row);
        await f.Model.InspectMemoryAsync();
        f.Model.MemoryDraft.Should().Be(" exact e\u0301 value ");
        f.Model.MemoryClass.Should().Be(MemoryContentClass.ResponsePreference);
        await f.Model.ReviewMemoryAsync(true);
        f.Model.MemoryRecords.Should().BeEmpty();
        AssertRetired(f.Model, emptyDetail: false);
        f.Model.MemoryReviewFilter = MemoryReviewState.Reviewed;
        row = f.Model.MemoryRecords.Single();
        f.Model.SelectMemory(row);
        f.Model.CanAdmitMemory.Should().BeFalse();
        await f.Model.InspectMemoryAsync();
        f.Model.CanAdmitMemory.Should().BeTrue();
        f.Model.ClearMemoryFilters();
        AssertRetired(f.Model);
        f.Model.SelectMemory(row);
        await f.Model.InspectMemoryAsync();
        await f.Model.AdmitMemoryAsync();
        await f.Model.DisableMemoryAsync();
        f.Model.MemoryRecords.Single().Retention.Should().Be(MemoryRetentionState.Disabled);
        f.Model.MemoryDraft = "replacement exact bytes";
        await f.Model.EditMemoryAsync();
        f.Model.MemoryRecords.Single().Review.Should().Be(MemoryReviewState.Proposed);
        f.Model.CanReviewMemory.Should().BeFalse();
        await f.Model.InspectMemoryAsync();
        f.Model.MemoryDraft.Should().Be("replacement exact bytes");
        await f.Model.ReviewMemoryAsync(false);
        f.Model.MemoryRecords.Single().Review.Should().Be(MemoryReviewState.Rejected);
        await f.Model.ForgetMemoryAsync();
        f.Model.MemoryRetentionFilter = MemoryRetentionState.Forgotten;
        var forgotten = f.Model.MemoryRecords.Single();
        f.Model.SelectMemory(forgotten);
        await f.Model.InspectMemoryAsync();
        f.Model.MemoryDraft.Should().BeEmpty();
        f.Model.MemoryDetail.Should().NotContain("exact e").And.NotContain("replacement exact bytes");
        f.Model.CanEditMemory.Should().BeFalse();
        f.Model.CanReviewMemory.Should().BeFalse();
        f.Model.CanAdmitMemory.Should().BeFalse();
        f.Model.MemoryIdFilter = forgotten.Id.ToString("D");
        await f.CreateMemory("proposed");
        f.Model.MemoryRecords.Should().Equal([forgotten], "the listed source is frozen until an explicit action replaces it");
        f.Model.SelectMemory(forgotten);
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryRecords.Should().Equal(forgotten);
        f.Model.MemoryIdFilter.Should().Be(forgotten.Id.ToString("D"));
        AssertRetired(f.Model, emptyDetail: false);
        f.Model.ClearMemoryFilters();
        f.Model.MemoryRecords.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("cancel-close")]
    public async Task SessionPrivacyControlClosureAndCancelledListCannotPublishLateContentOrRetainFilters(string boundary)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("proposed");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.MemoryIdFilter = row.Id.ToString("D");
        f.Model.SelectMemory(row);
        await f.Model.InspectMemoryAsync();
        if (boundary is "session") { await f.Model.SelectAsync(null); }
        else if (boundary is "privacy")
        {
            f.Access.CanControl = false;
            f.Model.MemoryRecords.Should().BeEmpty();
            await f.Model.ListMemoriesAsync();
        }
        else if (boundary is "control")
        {
            f.Access.ControlRevision++;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.SelectedMemory.Should().BeNull();
            f.Model.MemoryDetail.Should().BeEmpty();
            f.Model.MemoryDraft.Should().BeEmpty();
            f.Model.CanProposeMemory.Should().BeFalse();
            f.Model.CanEditMemory.Should().BeFalse();
            await f.Model.ListMemoriesAsync();
        }
        else if (boundary is "close") { f.Model.Close(); }
        else
        {
            f.Store.Hold = true;
            var refresh = f.Model.ListMemoriesAsync();
            await f.Store.Entered.Task.WaitAsync(f.Storage.Token);
            if (boundary is "cancel-close") { f.Model.Close(); }
            else { f.Model.MemoryReviewFilter = MemoryReviewState.Proposed; }
            AssertRetired(f.Model);
            f.Store.Release.SetResult();
            await refresh;
            f.Model.Status.Should().Contain("no", "cancellation or changed admission requires explicit recovery");
        }
        if (boundary is "control")
        {
            f.Model.MemoryRecords.Should().BeEmpty("control changes discard volatile proposals instead of recovering cached content");
            AssertRetired(f.Model, emptyDetail: false);
        }
        else
        {
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.CanFilterMemories.Should().BeFalse();
            f.Model.MemoryIdFilter.Should().BeEmpty();
            AssertRetired(f.Model);
        }
    }

    [WindowsFact]
    public async Task NativeFiltersHaveTypedAnyChoicesKeyboardFocusAccessibleNamesAndClearWithoutIo()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            await f.CreateMemory("proposed");
            await f.Model.ListMemoriesAsync();
            var window = new SessionsWindow(f.Model);
            window.Show();
            try
            {
                var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab =>
                    string.Equals(tab.Header?.ToString(), "Session memories", StringComparison.Ordinal));
                window.Activate();
                window.UpdateLayout();
                var id = window.FindControl<TextBox>("MemoryIdFilter")!;
                var review = window.FindControl<ComboBox>("MemoryReviewFilter")!;
                var retention = window.FindControl<ComboBox>("MemoryRetentionFilter")!;
                var clear = window.FindControl<Button>("ClearMemoryFilters")!;
                id.Focus().Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(id)!.GetName().Should().Contain("metadata only");
                ControlAutomationPeer.CreatePeerForElement(review)!.GetName().Should().Contain("Review").And.Contain("Any");
                ControlAutomationPeer.CreatePeerForElement(retention)!.GetName().Should().Contain("Retention").And.Contain("Any");
                ControlAutomationPeer.CreatePeerForElement(clear)!.GetName().Should().Contain("retire selection");
                review.Focus().Should().BeTrue();
                retention.Focus().Should().BeTrue();
                clear.Focus().Should().BeTrue();
                id.Focus().Should().BeTrue();
                var calls = f.Store.Calls;
                id.Text = f.Model.MemoryRecords.Single().Id.ToString("D");
                f.Model.MemoryIdFilter.Should().Be(id.Text);
                review.SelectedIndex = 2;
                f.Model.MemoryReviewFilter.Should().Be(MemoryReviewState.Reviewed);
                f.Model.MemoryRecords.Should().BeEmpty();
                retention.SelectedIndex = 2;
                f.Model.MemoryRetentionFilter.Should().Be(MemoryRetentionState.Enabled);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                id.IsFocused.Should().BeTrue();
                id.Text.Should().BeEmpty();
                f.Model.MemoryReviewFilter.Should().BeNull();
                f.Model.MemoryRetentionFilter.Should().BeNull();
                review.SelectedIndex.Should().Be(0);
                retention.SelectedIndex.Should().Be(0);
                id.Text = "invalid";
                id.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                id.Text.Should().BeEmpty();
                f.Model.MemoryRecords.Should().ContainSingle();
                f.Store.Calls.Should().Be(calls);
            }
            finally { window.Close(); }
        });
    }

    private static void AssertRetired(SessionsViewModel model, bool emptyDetail = true)
    {
        model.SelectedMemory.Should().BeNull();
        model.MemoryDraft.Should().BeEmpty();
        if (emptyDetail) { model.MemoryDetail.Should().BeEmpty(); }
        model.CanInspectMemory.Should().BeFalse();
        model.CanReviewMemory.Should().BeFalse();
        model.CanAdmitMemory.Should().BeFalse();
        model.CanEditMemory.Should().BeFalse();
        model.CanDisableMemory.Should().BeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal Access Access { get; } = new();
        internal CountingStore Store { get; private set; } = null!;
        internal MemoryManagementService Management { get; private set; } = null!;
        internal SessionsViewModel Model { get; private set; } = null!;
        internal RecordingLogger Logger { get; } = new();
        internal int Activities { get; private set; }
        private ActivityListener? listener;

        internal static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.Storage.InitializeAsync();
            f.Store = new(f.Storage.Store);
            var sessions = new SessionWorkspaceService(f.Storage.Store, new(f.Storage.Tasks), f.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            f.Management = new(sessions, f.Storage.Store, f.Access, f.Access, f.Store,
                new Audit(), NullLogger<MemoryAdmissionService>.Instance, f.Storage.Time);
            var sink = new WindowsSqliteEvidenceSink(f.Storage.Paths);
            sink.Initialize();
            f.Model = new(sessions, new(new WindowsSqliteEvidenceReader(sink), f.Access, f.Storage.Time,
                NullLogger<DurableEvidenceQuery>.Instance), f.Access, f.Logger, memories: f.Management);
            f.listener = new()
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = _ => f.Activities++,
            };
            ActivitySource.AddActivityListener(f.listener);
            await f.Model.RefreshAsync();
            await f.Model.SelectAsync(f.Model.Sessions.Single());
            return f;
        }

        internal async Task<MemorySummary> CreateMemory(string state)
        {
            var row = (await Management.ExecuteAsync(new(MemoryCommandOperation.Propose, Storage.Request.SessionId.Value,
                Candidate: new(MemoryContentClass.Decision, "private exact value")),
                RequestOrigin.LocalUi, () => true, Storage.Token)).Memories.Single();
            async Task Act(MemoryCommandOperation operation, bool accept = true)
            {
                var result = await Management.ExecuteAsync(new(operation, Storage.Request.SessionId.Value, row.Id, row.Revision, accept),
                    RequestOrigin.LocalUi, () => true, Storage.Token);
                row = result.Memories.Single();
            }
            if (state is not "proposed")
            {
                await Act(MemoryCommandOperation.Inspect);
                await Act(MemoryCommandOperation.Review, state is not "rejected");
                if (state is "enabled" or "disabled" or "forgotten")
                {
                    await Act(MemoryCommandOperation.Admit);
                    if (state is "disabled") { await Act(MemoryCommandOperation.Disable); }
                    if (state is "forgotten") { await Act(MemoryCommandOperation.Forget); }
                }
            }
            return row;
        }

        public async ValueTask DisposeAsync()
        {
            Model.Close();
            await Management.DisposeAsync();
            listener?.Dispose();
            Storage.Dispose();
        }
    }

    private sealed class CountingStore(IMemoryStore inner) : IMemoryStore
    {
        internal int Calls { get; private set; }
        internal bool Hold { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token)
        {
            Calls++;
            if (Hold) { Entered.SetResult(); await Release.Task.WaitAsync(token); }
            return await inner.ReadMemoryBoundaryAsync(request, controlRevision, token);
        }

        public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            return inner.TransactMemoryAsync(request, boundary, transition, admitted, token);
        }
    }

    private sealed class Access : ISessionWorkspaceAccess, ICapabilityHostAccess, IEvidenceQueryAccess
    {
        public bool CanInspect => true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
        public bool IsCurrentHost => true;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => HostActivity.RequireCurrent();
    }

    private sealed class RecordingLogger : ILogger<SessionsViewModel>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
