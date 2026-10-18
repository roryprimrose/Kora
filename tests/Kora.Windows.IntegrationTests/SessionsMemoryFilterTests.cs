using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

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
public sealed partial class SessionsMemoryFilterTests
{
    private static readonly string[] RangeControlNames = ["MemoryMinimumRevisionFilter", "MemoryMaximumRevisionFilter",
        "MemoryCreatedFromFilter", "MemoryCreatedUntilFilter"];

    [WindowsFact]
    public async Task EveryTypedStateAndCombinedExactGuidFilterUsesOnlyTheFrozenContentFreeSnapshot()
    {
        await using var f = await Fixture.Create();
        f.Model.CanFilterMemories.Should().BeFalse();
        f.Model.MemoryIdFilter = "not admitted before List";
        f.Model.MemoryScopeIdentityFilter = "not admitted before List";
        f.Model.MemoryScopeFilter = MemoryScopeKind.Source;
        f.Model.MemoryOrder = MemorySnapshotOrder.CreatedNewest;
        f.Model.MemoryMinimumRevisionFilter = "1";
        f.Model.MemoryMaximumRevisionFilter = "2";
        f.Model.MemoryCreatedFromFilter = "not admitted";
        f.Model.MemoryCreatedUntilFilter = "not admitted";
        f.Model.MemoryIdFilter.Should().BeEmpty();
        f.Model.MemoryScopeIdentityFilter.Should().BeEmpty();
        f.Model.MemoryScopeFilter.Should().BeNull();
        f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.Source);
        f.Model.MemoryOrderChoice.Should().BeSameAs(f.Model.MemoryOrders[0]);
        f.Model.MemoryOrders.Select(option => option.Value).Should().Equal(Enum.GetValues<MemorySnapshotOrder>());
        f.Model.MemoryOrders.Select(option => option.ToString()).Should()
            .Equal("Source order (default)", "Created oldest", "Created newest", "Revision lowest", "Revision highest");
        AssertEmptyRanges(f.Model);
        f.Model.MemoryScopeChoice.Should().BeSameAs(f.Model.MemoryScopeFilters[0]);
        foreach (var state in new[] { "proposed", "reviewed", "rejected", "enabled", "disabled", "forgotten" })
        {
            await f.CreateMemory(state);
        }
        await f.Model.ListMemoriesAsync();
        var snapshot = f.Model.MemoryRecords.ToArray();
        snapshot.Should().HaveCount(6);
        MemoryReviewState?[] reviews = [null, .. Enum.GetValues<MemoryReviewState>().Select(value => (MemoryReviewState?)value)];
        MemoryScopeKind?[] scopes = [null, .. Enum.GetValues<MemoryScopeKind>().Select(value => (MemoryScopeKind?)value)];
        MemoryRetentionState?[] retentions = [null, .. Enum.GetValues<MemoryRetentionState>().Select(value => (MemoryRetentionState?)value)];
        f.Model.MemoryReviewFilters.Select(option => option.Value).Should()
            .Equal(reviews);
        f.Model.MemoryRetentionFilters.Select(option => option.Value).Should()
            .Equal(retentions);
        f.Model.MemoryReviewFilters[0].ToString().Should().Be("Any");
        f.Model.MemoryScopeFilters.Select(option => option.Value).Should().Equal(scopes);
        f.Model.MemoryScopeFilters[0].ToString().Should().Be("Any");
        f.Model.MemoryRetentionFilters[0].ToString().Should().Be("Any");
        var calls = f.Store.Calls;
        var activities = f.Activities;
        var logs = f.Logger.Messages.ToArray();
        var bytes = File.ReadAllBytes(f.Storage.DatabasePath);
        var clock = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Storage.Token);
        foreach (var order in Enum.GetValues<MemorySnapshotOrder>())
        foreach (var scope in scopes)
        {
            f.Model.MemoryOrder = order;
            f.Model.MemoryScopeFilter = scope;
            foreach (var review in reviews)
            {
                foreach (var retention in retentions)
                {
                    f.Model.MemoryReviewFilter = review;
                    f.Model.MemoryRetentionFilter = retention;
                    f.Model.MemoryRecords.Should().Equal(ExpectedOrder(snapshot.Where(row =>
                        (scope is null || row.Scope.Kind == scope)
                        && (review is null || row.Review == review) && (retention is null || row.Retention == retention)), order));
                    foreach (var row in snapshot)
                    {
                        f.Model.MemoryIdFilter = row.Id.ToString("D").ToUpperInvariant();
                        f.Model.MemoryScopeIdentityFilter = row.Scope.Identity.ToString("D").ToUpperInvariant();
                        f.Model.MemoryRecords.Should().Equal(ExpectedOrder(snapshot.Where(candidate =>
                            candidate.Id == row.Id && (review is null || candidate.Review == review)
                            && candidate.Scope.Identity == row.Scope.Identity
                            && (scope is null || candidate.Scope.Kind == scope)
                            && (retention is null || candidate.Retention == retention)), order));
                    }
                    f.Model.MemoryIdFilter = string.Empty;
                    f.Model.MemoryScopeIdentityFilter = string.Empty;
                }
            }
        }
        f.Model.MemoryOrder = (MemorySnapshotOrder)99;
        f.Model.MemoryOrderChoice.Value.Should().Be((MemorySnapshotOrder)99);
        f.Model.MemoryOrderChoice.ToString().Should().Be("Invalid order");
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().Contain("Unknown memory snapshot order").And.Contain("Clear filters");
        f.Model.ClearMemoryFilters();
        f.Model.MemoryScopeIdentityFilter = "abcdefab-1234-4567-89ab-abcdefabcdef";
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().StartWith("0 of 6");
        foreach (var input in new[] { "abc", " ", Guid.Empty.ToString("D"), snapshot[0].Scope.Identity.ToString("N"),
            snapshot[0].Scope.Identity.ToString("B"), " " + snapshot[0].Scope.Identity.ToString("D"),
            snapshot[0].Scope.Identity.ToString("D") + "\n" })
        {
            f.Model.MemoryScopeIdentityFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("canonical D memory scope identity");
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
        f.Model.MemoryScopeFilter = (MemoryScopeKind)99;
        f.Model.MemoryScopeChoice.Value.Should().Be((MemoryScopeKind)99);
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.MemoryFilterStatus.Should().Contain("Unknown").And.Contain("Clear filters");
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
        foreach (var row in snapshot)
        {
            f.Model.ClearMemoryFilters();
            f.Model.MemoryIdFilter = row.Id.ToString("D");
            f.Model.MemoryScopeIdentityFilter = row.Scope.Identity.ToString("D");
            f.Model.MemoryScopeFilter = row.Scope.Kind;
            f.Model.MemoryReviewFilter = row.Review;
            f.Model.MemoryRetentionFilter = row.Retention;
            f.Model.MemoryMinimumRevisionFilter = row.Revision.ToString(CultureInfo.InvariantCulture);
            f.Model.MemoryMaximumRevisionFilter = row.Revision.ToString(CultureInfo.InvariantCulture);
            f.Model.MemoryCreatedFromFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            f.Model.MemoryCreatedUntilFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            foreach (var order in Enum.GetValues<MemorySnapshotOrder>())
            {
                f.Model.MemoryOrder = order;
                f.Model.MemoryRecords.Should().Equal(row);
            }
            f.Model.MemoryRecords.Should().Equal([row], "both revision and UTC endpoints are inclusive and AND with every exact predicate");
            f.Model.MemoryMinimumRevisionFilter = (row.Revision + 1).ToString(CultureInfo.InvariantCulture);
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("minimum must be <= maximum");
            f.Model.MemoryMaximumRevisionFilter = string.Empty;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().StartWith("0 of 6");
            f.Model.MemoryMinimumRevisionFilter = string.Empty;
            f.Model.MemoryCreatedUntilFilter = string.Empty;
            f.Model.MemoryCreatedFromFilter = row.CreatedAt.AddTicks(1).ToString("O", CultureInfo.InvariantCulture);
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryCreatedFromFilter = string.Empty;
            f.Model.MemoryCreatedUntilFilter = row.CreatedAt.AddTicks(-1).ToString("O", CultureInfo.InvariantCulture);
            f.Model.MemoryRecords.Should().BeEmpty();
        }
        foreach (var input in new[] { "0", "-1", "1 ", "9223372036854775808" })
        {
            f.Model.ClearMemoryFilters();
            f.Model.MemoryMinimumRevisionFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().NotStartWith("0 of");
            f.Model.ClearMemoryFilters();
            f.Model.MemoryMaximumRevisionFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().NotStartWith("0 of");
        }
        foreach (var input in new[] { " ", "1970-01-01", "1970-01-01T00:00:00.0000000+01:00" })
        {
            f.Model.ClearMemoryFilters();
            f.Model.MemoryCreatedFromFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("canonical UTC");
            f.Model.ClearMemoryFilters();
            f.Model.MemoryCreatedUntilFilter = input;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("canonical UTC");
        }
        f.Model.ClearMemoryFilters();
        f.Model.MemoryRecords.Should().Equal(snapshot);
        AssertEmptyRanges(f.Model);
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
    [InlineData("scope")]
    [InlineData("scopeIdentity")]
    [InlineData("review")]
    [InlineData("retention")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("from")]
    [InlineData("until")]
    [InlineData("clear")]
    [InlineData("order")]
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
            case "scope": f.Model.MemoryScopeFilter = row.Scope.Kind; break;
            case "scopeIdentity": f.Model.MemoryScopeIdentityFilter = row.Scope.Identity.ToString("D"); break;
            case "review": f.Model.MemoryReviewFilter = row.Review; break;
            case "retention": f.Model.MemoryRetentionFilter = row.Retention; break;
            case "minimum": f.Model.MemoryMinimumRevisionFilter = row.Revision.ToString(CultureInfo.InvariantCulture); break;
            case "maximum": f.Model.MemoryMaximumRevisionFilter = row.Revision.ToString(CultureInfo.InvariantCulture); break;
            case "from": f.Model.MemoryCreatedFromFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture); break;
            case "until": f.Model.MemoryCreatedUntilFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture); break;
            case "clear": f.Model.ClearMemoryFilters(); break;
            case "order": f.Model.MemoryOrder = MemorySnapshotOrder.CreatedNewest; break;
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
        f.Model.MemoryOrder = MemorySnapshotOrder.RevisionHighest;
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryRecords.Should().Equal(forgotten);
        f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.RevisionHighest);
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
    [InlineData("cancel-order")]
    [InlineData("cancel-close")]
    public async Task SessionPrivacyControlClosureAndCancelledListCannotPublishLateContentOrRetainFilters(string boundary)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("proposed");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.MemoryIdFilter = row.Id.ToString("D");
        f.Model.MemoryScopeIdentityFilter = row.Scope.Identity.ToString("D");
        f.Model.MemoryScopeFilter = MemoryScopeKind.Session;
        f.Model.MemoryMinimumRevisionFilter = "1";
        f.Model.MemoryMaximumRevisionFilter = long.MaxValue.ToString(CultureInfo.InvariantCulture);
        f.Model.MemoryCreatedFromFilter = DateTimeOffset.MinValue.ToString("O", CultureInfo.InvariantCulture);
        f.Model.MemoryCreatedUntilFilter = DateTimeOffset.MaxValue.ToString("O", CultureInfo.InvariantCulture);
        f.Model.MemoryOrder = MemorySnapshotOrder.RevisionHighest;
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
            else if (boundary is "cancel-order") { f.Model.MemoryOrder = MemorySnapshotOrder.CreatedNewest; }
            else { f.Model.MemoryScopeIdentityFilter = "abcdefab-1234-4567-89ab-abcdefabcdef"; }
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
            f.Model.MemoryScopeFilter.Should().BeNull();
            f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.Source);
            AssertEmptyRanges(f.Model);
            AssertRetired(f.Model);
            var calls = f.Store.Calls;
            f.Model.MemoryOrder = MemorySnapshotOrder.CreatedNewest;
            f.Model.ClearMemoryFilters();
            f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.Source);
            f.Store.Calls.Should().Be(calls, "closed or stale source controls cannot read or revive state");
        }
    }

    [WindowsFact]
    public async Task NativeFiltersHaveTypedAnyChoicesKeyboardFocusAccessibleNamesAndClearWithoutIo()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
            await f.Model.RefreshWorkAsync();
            await f.CreateMemory("proposed");
            await f.Model.ListMemoriesAsync();
            var window = new SessionsWindow(f.Model);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Show();
            try
            {
                var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                var memoryTab = tabs.Items.OfType<TabItem>().Single(tab =>
                    string.Equals(tab.Header?.ToString(), "Session memories", StringComparison.Ordinal));
                tabs.SelectedItem = memoryTab;
                window.Activate();
                window.UpdateLayout();
                AssertVisibleInWindow(memoryTab, window);
                var id = window.FindControl<TextBox>("MemoryIdFilter")!;
                var scopeIdentity = window.FindControl<TextBox>("MemoryScopeIdentityFilter")!;
                var review = window.FindControl<ComboBox>("MemoryReviewFilter")!;
                var scope = window.FindControl<ComboBox>("MemoryScopeFilter")!;
                var retention = window.FindControl<ComboBox>("MemoryRetentionFilter")!;
                var order = window.FindControl<ComboBox>("MemoryOrder")!;
                var clear = window.FindControl<Button>("ClearMemoryFilters")!;
                order.BringIntoView();
                window.UpdateLayout();
                order.IsEnabled.Should().BeTrue();
                order.IsEffectivelyVisible.Should().BeTrue();
                order.Bounds.Width.Should().BeGreaterThan(0).And.BeLessThan(window.ClientSize.Width);
                var position = order.TranslatePoint(default, window)!.Value;
                position.X.Should().BeGreaterThanOrEqualTo(0);
                position.Y.Should().BeGreaterThanOrEqualTo(0);
                (position.X + order.Bounds.Width).Should().BeLessThanOrEqualTo(window.ClientSize.Width);
                (position.Y + order.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                order.SelectedIndex.Should().Be(0);
                order.SelectionBoxItem!.ToString().Should().Be("Source order (default)");
                ControlAutomationPeer.CreatePeerForElement(order)!.GetName().Should()
                    .Contain("Source order default").And.Contain("metadata only").And.Contain("not freshness or authority");
                ControlAutomationPeer.CreatePeerForElement(order)!.GetProvider<ISelectionProvider>()!
                    .GetSelection().Should().ContainSingle();
                var rangeFields = RangeControlNames
                    .Select(name => window.FindControl<TextBox>(name)!).ToArray();
                foreach (var range in rangeFields)
                {
                    range.IsEnabled.Should().BeTrue();
                    range.Focus().Should().BeTrue();
                    ControlAutomationPeer.CreatePeerForElement(range)!.GetName().Should()
                        .Contain("inclusive").And.Contain("Any").And.Contain("metadata only");
                }
                id.Focus().Should().BeTrue();
                scope.SelectedIndex.Should().Be(0);
                scope.SelectedItem.Should().BeSameAs(f.Model.MemoryScopeFilters[0]);
                scope.SelectionBoxItem!.ToString().Should().Be("Any");
                review.SelectedIndex.Should().Be(0);
                retention.SelectedIndex.Should().Be(0);
                ControlAutomationPeer.CreatePeerForElement(scope)!.GetName().Should()
                    .Contain("Scope").And.Contain("Any").And.Contain("not permission");
                var scopeSelection = ControlAutomationPeer.CreatePeerForElement(scope)!.GetProvider<ISelectionProvider>()!;
                scopeSelection.Should().NotBeNull();
                scopeSelection.GetSelection().Should().ContainSingle();
                ControlAutomationPeer.CreatePeerForElement(id)!.GetName().Should().Contain("metadata only");
                scopeIdentity.IsEnabled.Should().BeTrue();
                scopeIdentity.Focus().Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(scopeIdentity)!.GetName().Should()
                    .Contain("scope identity").And.Contain("canonical D").And.Contain("Any")
                    .And.Contain("snapshot metadata only").And.Contain("not permission, ownership or scope authority");
                ControlAutomationPeer.CreatePeerForElement(review)!.GetName().Should().Contain("Review").And.Contain("Any");
                ControlAutomationPeer.CreatePeerForElement(retention)!.GetName().Should().Contain("Retention").And.Contain("Any");
                ControlAutomationPeer.CreatePeerForElement(clear)!.GetName().Should().Contain("retire selection");
                review.Focus().Should().BeTrue();
                scope.Focus().Should().BeTrue();
                retention.Focus().Should().BeTrue();
                clear.Focus().Should().BeTrue();
                id.Focus().Should().BeTrue();
                var calls = f.Store.Calls;
                var row = f.Model.MemoryRecords.Single();
                foreach (var choice in new[] { order, scope, review, retention })
                {
                    f.Model.SelectMemory(row);
                    await f.Model.InspectMemoryAsync();
                    f.Model.MemoryDraft = "unsubmitted replacement";
                    calls = f.Store.Calls;
                    choice.Focus().Should().BeTrue();
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    AssertRetired(f.Model);
                    if (choice == order) { f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.CreatedOldest); }
                    choice.IsDropDownOpen = true;
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    choice.IsDropDownOpen.Should().BeFalse();
                    choice.IsFocused.Should().BeTrue();
                    f.Model.MemoryOrder.Should().Be(MemorySnapshotOrder.Source);
                    order.SelectedIndex.Should().Be(0);
                    f.Model.MemoryRecords.Should().Equal(row);
                    AssertRetired(f.Model);
                    f.Store.Calls.Should().Be(calls);
                }
                scopeIdentity.Focus().Should().BeTrue();
                scopeIdentity.Text = row.Scope.Identity.ToString("D").ToUpperInvariant();
                f.Model.MemoryScopeIdentityFilter.Should().Be(scopeIdentity.Text);
                f.Model.MemoryRecords.Should().Equal(row);
                scopeIdentity.Text = "invalid";
                f.Model.MemoryRecords.Should().BeEmpty();
                scopeIdentity.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                scopeIdentity.IsFocused.Should().BeTrue();
                scopeIdentity.Text.Should().BeEmpty();
                f.Model.MemoryRecords.Should().Equal(row);
                string[] values = [row.Revision.ToString(CultureInfo.InvariantCulture),
                    row.Revision.ToString(CultureInfo.InvariantCulture),
                    row.CreatedAt.ToString("O", CultureInfo.InvariantCulture), row.CreatedAt.ToString("O", CultureInfo.InvariantCulture)];
                for (var index = 0; index < rangeFields.Length; index++)
                {
                    var range = rangeFields[index];
                    range.Focus().Should().BeTrue();
                    range.Text = values[index];
                    f.Model.MemoryRecords.Should().ContainSingle();
                    range.Text = "invalid";
                    f.Model.MemoryRecords.Should().BeEmpty();
                    range.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                    range.IsFocused.Should().BeTrue();
                    rangeFields.Should().OnlyContain(field => field.Text == string.Empty);
                    AssertEmptyRanges(f.Model);
                    f.Model.MemoryRecords.Should().ContainSingle();
                }
                scope.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                f.Model.MemoryScopeFilter.Should().Be(MemoryScopeKind.Unknown);
                f.Model.MemoryRecords.Should().BeEmpty();
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                f.Model.MemoryScopeFilter.Should().BeNull();
                id.Text = f.Model.MemoryRecords.Single().Id.ToString("D");
                f.Model.MemoryIdFilter.Should().Be(id.Text);
                scope.SelectedIndex = 4;
                f.Model.MemoryScopeFilter.Should().Be(MemoryScopeKind.Project);
                f.Model.MemoryRecords.Should().BeEmpty();
                review.SelectedIndex = 2;
                f.Model.MemoryReviewFilter.Should().Be(MemoryReviewState.Reviewed);
                f.Model.MemoryRecords.Should().BeEmpty();
                retention.SelectedIndex = 2;
                f.Model.MemoryRetentionFilter.Should().Be(MemoryRetentionState.Enabled);
                scopeIdentity.Text = "invalid";
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                id.IsFocused.Should().BeTrue();
                id.Text.Should().BeEmpty();
                scopeIdentity.Text.Should().BeEmpty();
                f.Model.MemoryReviewFilter.Should().BeNull();
                f.Model.MemoryScopeFilter.Should().BeNull();
                scope.SelectedIndex.Should().Be(0);
                f.Model.MemoryRetentionFilter.Should().BeNull();
                review.SelectedIndex.Should().Be(0);
                retention.SelectedIndex.Should().Be(0);
                id.Text = "invalid";
                id.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                id.Text.Should().BeEmpty();
                f.Model.MemoryRecords.Should().ContainSingle();
                f.Store.Calls.Should().Be(calls);
                foreach (var size in new[] { new Size(900, 700), new Size(1180, 900) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    window.UpdateLayout();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    AssertVisibleInWindow(memoryTab, window);
                    foreach (var control in new Control[] { order, clear })
                    {
                        control.Focus().Should().BeTrue();
                        AssertVisibleInWindow(control, window);
                    }
                    var pane = window.FindControl<ScrollViewer>("SelectedSessionWorkPane")!;
                    pane.Viewport.Height.Should().BeGreaterThan(0);
                    pane.Extent.Height.Should().BeGreaterThanOrEqualTo(960).And.BeLessThan(double.PositiveInfinity);
                    pane.Viewport.Width.Should().BeLessThanOrEqualTo(window.ClientSize.Width);
                    tabs.Bounds.Height.Should().BeGreaterThan(0);
                    var actions = window.FindControl<WrapPanel>("SessionWorkActions")!;
                    actions.Bounds.Width.Should().BeLessThanOrEqualTo(pane.Viewport.Width);
                    var buttons = actions.GetLogicalDescendants().OfType<Button>().ToArray();
                    buttons.Select(button => button.Name).Should().Equal("ReadQueue", "InspectWork", "CancelQueueEntry",
                        "RemoveQueueEntry", "MoveQueueUp", "MoveQueueDown", "EnqueueVersion", "EnqueueCapabilities",
                        "EnqueueReadiness", "EnqueueRuntimes", "EnqueueCapability", "EnqueueRuntimeStatus",
                        "DispatchQueue", "PauseQueue", "ResumeQueue", "ClearQueue");
                    foreach (var button in buttons)
                    {
                        if (button.IsEnabled) { button.Focus().Should().BeTrue(); }
                        AssertVisibleInWindow(button, window);
                    }
                    var enabled = buttons.Where(button => button.IsEnabled).ToArray();
                    enabled.Should().NotBeEmpty();
                    enabled[0].Focus().Should().BeTrue();
                    foreach (var button in enabled.Skip(1))
                    {
                        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                        button.IsFocused.Should().BeTrue();
                        window.UpdateLayout();
                        AssertVisibleInWindow(button, window);
                    }
                    var questions = window.GetLogicalDescendants().OfType<ItemsControl>().Single(control =>
                        Avalonia.Automation.AutomationProperties.GetName(control)
                            ?.StartsWith("Pending questions remain visible", StringComparison.Ordinal) == true);
                    f.Model.PendingQuestions.Should().NotBeEmpty();
                    var questionFields = questions.GetVisualDescendants().OfType<TextBlock>().ToArray();
                    questionFields.Should().NotBeEmpty();
                    foreach (var field in questionFields) { AssertVisibleInWindow(field, window); }
                    var nested = questions.GetVisualAncestors().OfType<ScrollViewer>().Skip(1).First();
                    nested.Extent.Height.Should().BeGreaterThan(nested.Viewport.Height);
                    var offset = nested.Offset.Y;
                    var wheelPoint = questionFields[^1].TranslatePoint(new Rect(questionFields[^1].Bounds.Size).Center, window)!.Value;
                    window.MouseWheel(wheelPoint, new Vector(0, 1), RawInputModifiers.None);
                    window.UpdateLayout();
                    nested.Offset.Y.Should().BeLessThan(offset);
                    AssertVisibleInWindow(questionFields[^1], window);
                    AssertVisibleInWindow(memoryTab, window);
                    order.Focus().Should().BeTrue();
                    AssertVisibleInWindow(order, window);
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                    order.IsFocused.Should().BeTrue();
                    f.Store.Calls.Should().Be(calls);
                }
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

    private static void AssertVisibleInWindow(Control control, Window window)
    {
        control.BringIntoView();
        window.UpdateLayout();
        // Scroll inner-to-outer using the updated descendant geometry at each native viewport.
        foreach (var viewport in control.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            viewport.BringDescendantIntoView(control, new Rect(control.Bounds.Size));
            window.UpdateLayout();
        }
        var location = control.TranslatePoint(default, window)!.Value;
        control.Bounds.Width.Should().BeGreaterThan(0);
        control.Bounds.Height.Should().BeGreaterThan(0);
        location.X.Should().BeGreaterThanOrEqualTo(0);
        location.Y.Should().BeGreaterThanOrEqualTo(0);
        (location.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(window.ClientSize.Width);
        (location.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height,
            $"control {control.Name ?? control.GetType().Name} at {location} with bounds {control.Bounds}; ancestors "
            + string.Join("; ", control.GetVisualAncestors().Select(visual => $"{visual.GetType().Name} {visual.Bounds}")));
        foreach (var viewport in control.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            var point = control.TranslatePoint(default, viewport)!.Value;
            point.X.Should().BeGreaterThanOrEqualTo(0);
            point.Y.Should().BeGreaterThanOrEqualTo(0);
            (point.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(viewport.Bounds.Width);
            (point.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(viewport.Bounds.Height,
                $"control {control.Name ?? control.GetType().Name} must be inside every enclosing scroll viewport");
        }
    }

    private static void AssertEmptyRanges(SessionsViewModel model)
    {
        model.MemoryScopeIdentityFilter.Should().BeEmpty();
        model.MemoryMinimumRevisionFilter.Should().BeEmpty();
        model.MemoryMaximumRevisionFilter.Should().BeEmpty();
        model.MemoryCreatedFromFilter.Should().BeEmpty();
        model.MemoryCreatedUntilFilter.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task MalformedSnapshotRowsCannotHideBehindScopeExclusionOrLeavePartialResults()
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.SelectMemory(row);
        await f.Model.InspectMemoryAsync();
        var calls = f.Store.Calls;
        var field = typeof(SessionsViewModel).GetField("memoryRows",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var malformed in new[] { row, row with { Id = Guid.Empty }, row with { Revision = 0 },
            row with { Scope = null! }, row with { Review = (MemoryReviewState)99 },
            row with { Retention = (MemoryRetentionState)99 },
            row with { CreatedAt = row.CreatedAt.ToOffset(TimeSpan.FromHours(-1)) } })
        {
            field.SetValue(f.Model, new[] { row, malformed });
            f.Model.ClearMemoryFilters();
            f.Model.MemoryScopeFilter = MemoryScopeKind.Source;
            f.Model.MemoryScopeIdentityFilter = "abcdefab-1234-4567-89ab-abcdefabcdef";
            f.Model.MemoryMinimumRevisionFilter = long.MaxValue.ToString(CultureInfo.InvariantCulture);
            f.Model.MemoryCreatedFromFilter = DateTimeOffset.MaxValue.ToString("O", CultureInfo.InvariantCulture);
            f.Model.MemoryOrder = MemorySnapshotOrder.CreatedNewest;
            f.Model.MemoryRecords.Should().BeEmpty();
            f.Model.MemoryFilterStatus.Should().Contain("memory snapshot").And.Contain("List explicitly");
            AssertRetired(f.Model);
        }
        f.Store.Calls.Should().Be(calls);
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryRecords.Should().BeEmpty("Source is a metadata predicate, not access to another source");
        f.Model.MemoryFilterStatus.Should().StartWith("0 of 1");
        f.Model.ClearMemoryFilters();
        f.Model.MemoryRecords.Should().Equal(row);
    }

    [Theory]
    [InlineData(false, "scope")]
    [InlineData(true, "scope")]
    [InlineData(false, "scopeIdentity")]
    [InlineData(true, "scopeIdentity")]
    [InlineData(false, "minimum")]
    [InlineData(true, "minimum")]
    [InlineData(false, "maximum")]
    [InlineData(true, "maximum")]
    [InlineData(false, "from")]
    [InlineData(true, "from")]
    [InlineData(false, "until")]
    [InlineData(true, "until")]
    [InlineData(false, "order")]
    [InlineData(true, "order")]
    public async Task FilterEditFencesLateInspectionAndFailedRefreshWithoutRevivingDetailOrReadiness(bool fail, string field)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.SelectMemory(row);
        await f.Model.InspectMemoryAsync();
        f.Model.MemoryDraft = "unsubmitted";
        f.Store.Hold = true;
        f.Store.Fail = fail;
        var pending = fail ? f.Model.ListMemoriesAsync() : f.Model.InspectMemoryAsync();
        await f.Store.Entered.Task.WaitAsync(f.Storage.Token);
        var calls = f.Store.Calls;
        switch (field)
        {
            case "scope": f.Model.MemoryScopeFilter = MemoryScopeKind.Session; break;
            case "scopeIdentity": f.Model.MemoryScopeIdentityFilter = row.Scope.Identity.ToString("D"); break;
            case "minimum": f.Model.MemoryMinimumRevisionFilter = "1"; break;
            case "maximum": f.Model.MemoryMaximumRevisionFilter = row.Revision.ToString(CultureInfo.InvariantCulture); break;
            case "from": f.Model.MemoryCreatedFromFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture); break;
            case "until": f.Model.MemoryCreatedUntilFilter = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture); break;
            case "order": f.Model.MemoryOrder = MemorySnapshotOrder.RevisionHighest; break;
        }
        AssertRetired(f.Model);
        f.Store.Calls.Should().Be(calls);
        f.Store.Release.SetResult();
        await pending;
        AssertRetired(f.Model);
        f.Model.MemoryRecords.Should().BeEmpty();
        f.Model.CanFilterMemories.Should().BeFalse();
        AssertEmptyRanges(f.Model);
        f.Model.Status.Should().Contain("unavailable/denied").And.Contain("Refresh durable state");
    }

    private static IEnumerable<MemorySummary> ExpectedOrder(IEnumerable<MemorySummary> rows, MemorySnapshotOrder order) =>
        order switch
        {
            MemorySnapshotOrder.Source => rows,
            MemorySnapshotOrder.CreatedOldest => rows.OrderBy(row => row.CreatedAt)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal),
            MemorySnapshotOrder.CreatedNewest => rows.OrderByDescending(row => row.CreatedAt)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal),
            MemorySnapshotOrder.RevisionLowest => rows.OrderBy(row => row.Revision)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal),
            MemorySnapshotOrder.RevisionHighest => rows.OrderByDescending(row => row.Revision)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal),
            _ => throw new InvalidOperationException("Test requires a defined order."),
        };

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
        internal bool Fail { get; set; }
        internal Action<MemoryResult>? AfterTransaction { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token)
        {
            Calls++;
            if (Hold) { Entered.SetResult(); await Release.Task.WaitAsync(token); }
            if (Fail) { throw new InvalidDataException("Memory storage is unavailable."); }
            return await inner.ReadMemoryBoundaryAsync(request, controlRevision, token);
        }

        public async ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            var result = await inner.TransactMemoryAsync(request, boundary, transition, admitted, token);
            AfterTransaction?.Invoke(result);
            return result;
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
