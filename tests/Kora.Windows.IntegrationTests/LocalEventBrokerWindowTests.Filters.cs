using System.Diagnostics;

using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalEventBrokerWindowTests
{
    [WindowsFact]
    public async Task NativeTypedFiltersComposeWithoutReadsWritesActivitiesOrQuestionAndWorkEffects()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
            var source = new PausedSource(f.Source());
            await using var broker = new LocalEventBroker(source, f.State, f.Access, f.Storage.Time,
                f, NullLogger<LocalEventBroker>.Instance);
            var viewer = f.Viewer(broker);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var rows = viewer.LocalEvents.ToArray();
                var work = viewer.WorkRecords.ToArray();
                var snapshotStatus = viewer.LocalEventStatus;
                var reads = source.Reads;
                var database = File.ReadAllBytes(f.Storage.DatabasePath);
                var preferences = File.ReadAllBytes(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json"));
                var before = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token);
                var activities = 0;
                var filterThread = Environment.CurrentManagedThreadId;
                using var listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStarted = _ =>
                    {
                        if (Environment.CurrentManagedThreadId == filterThread) { activities++; }
                    },
                };
                ActivitySource.AddActivityListener(listener);
                var categories = viewer.LocalEventCategoryFilters;
                var reasons = viewer.LocalEventReasonFilters;
                categories.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventCategory>().Select(value => (LocalEventCategory?)value)]);
                reasons.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventReason>().Select(value => (LocalEventReason?)value)]);
                categories[0].ToString().Should().Be("All");
                reasons[0].ToString().Should().Be("All");
                var categoryControl = window.FindControl<ComboBox>("LocalEventCategoryFilter")!;
                var reasonControl = window.FindControl<ComboBox>("LocalEventReasonFilter")!;
                ControlAutomationPeer.CreatePeerForElement(categoryControl)!.GetName().Should().Contain("authoritative category");
                ControlAutomationPeer.CreatePeerForElement(reasonControl)!.GetName().Should().Contain("exact delivery reason");
                foreach (var category in categories)
                {
                    foreach (var reason in reasons)
                    {
                        categoryControl.SelectedItem = category;
                        reasonControl.SelectedItem = reason;
                        viewer.LocalEvents.Should().Equal(rows.Where(new LocalEventFilter(category.Value, reason.Value).Matches));
                        viewer.SelectedLocalEvent.Should().BeNull();
                        AssertEventActionsDisabled(viewer, window);
                        viewer.LocalEventStatus.Should().Be(snapshotStatus);
                    }
                }
                viewer.LocalEventCategoryFilter = LocalEventCategory.Work;
                viewer.LocalEventFilterStatus.Should().Contain("No matching loaded events").And.Contain("0 of 1");
                viewer.LocalEventStatus.Should().Contain("omitted");
                viewer.LocalEventCategoryFilter = (LocalEventCategory)99;
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.LocalEvents.Should().BeEmpty();
                viewer.ClearLocalEventFilters();
                viewer.LocalEventReasonFilter = (LocalEventReason)99;
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.ClearLocalEventFilters();
                viewer.LocalEvents.Should().Equal(rows);
                source.Reads.Should().Be(reads);
                activities.Should().Be(0);
                File.ReadAllBytes(f.Storage.DatabasePath).Should().Equal(database);
                File.ReadAllBytes(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json")).Should().Equal(preferences);
                viewer.PendingQuestions.Single().Key.Should().Be(question.Key);
                viewer.WorkRecords.Should().Equal(work);
                broker.RoutineQuiet.Enabled.Should().BeFalse();
                listener.Dispose();
                (await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token)).Should().Be(before);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task FilterEditsRetireEvenStillVisibleSelectionAndReasonMutationCannotRetargetOrDropOtherSnapshotRows()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
            await using var broker = f.Broker();
            var viewer = f.Viewer(broker);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var row = viewer.LocalEvents.Single();
                viewer.SelectLocalEvent(row);
                await viewer.ReviewLocalEventAsync();
                viewer.Detail.Should().NotBeEmpty();
                row = viewer.LocalEvents.Single();
                viewer.LocalEventCategoryFilter = row.Event.Category;
                viewer.LocalEvents.Should().ContainSingle();
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(row with { Event = row.Event with { Revision = row.Event.Revision + 1 } });
                viewer.SelectedLocalEvent.Should().BeNull();
                viewer.SelectLocalEvent(row);
                viewer.LocalEventReasonFilter = row.Reason;
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(row);
                viewer.ClearLocalEventFilters();
                AssertEventActionsDisabled(viewer, window);
                viewer.LocalEventReasonFilter = row.Reason;
                viewer.SelectLocalEvent(row);
                await viewer.DismissLocalEventAsync();
                viewer.LocalEvents.Should().BeEmpty("the result no longer matches the exact reason");
                AssertEventActionsDisabled(viewer, window);
                viewer.ClearLocalEventFilters();
                var dismissed = viewer.LocalEvents.Single();
                dismissed.Reason.Should().Be(LocalEventReason.Dismissed);
                dismissed.Event.Id.Should().Be(row.Event.Id);
                dismissed.Event.Revision.Should().Be(row.Event.Revision + 1);
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(dismissed);
                viewer.CanControlLocalEvent.Should().BeTrue("new explicit selection remains usable without a new source read");
                await viewer.DeferLocalEventAsync();
                viewer.SelectedLocalEvent!.Reason.Should().Be(LocalEventReason.Deferred);
                await viewer.RefreshWorkAsync();
                viewer.SelectedLocalEvent.Should().BeNull();
                AssertEventActionsDisabled(viewer, window);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("workspace")]
    [InlineData("session")]
    [InlineData("close")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task PendingRefreshCannotRepublishAfterFilterSourcePrivacyFailureOrCancellationChanges(string change)
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        var source = new PausedSource(f.Source());
        await using var broker = new LocalEventBroker(source, f.State, f.Access, f.Storage.Time,
            f, NullLogger<LocalEventBroker>.Instance);
        var viewer = f.Viewer(broker);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        source.Pause = true;
        var pending = viewer.RefreshWorkAsync();
        await source.Entered.Task;
        viewer.SelectedLocalEvent.Should().BeNull("refresh revokes at entry, not only after a successful read");
        viewer.CanControlLocalEvent.Should().BeFalse();
        switch (change)
        {
            case "filter": viewer.LocalEventCategoryFilter = LocalEventCategory.Attention; break;
            case "privacy": f.Access.CanInspect = false; f.Access.CanControl = false; break;
            case "revision": f.Access.ControlRevision++; break;
            case "workspace": viewer.RevokeSessionList(); break;
            case "session": viewer.HistorySessionId = Guid.NewGuid().ToString("D"); break;
            case "close": viewer.Close(); break;
            case "failure": source.Failure = new IOException("test source unavailable"); break;
            case "cancel": source.Failure = new OperationCanceledException("test cancellation"); break;
        }
        source.Release.TrySetResult();
        await pending;
        viewer.LocalEvents.Should().BeEmpty();
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.CanControlLocalEvent.Should().BeFalse();
        viewer.LocalEventFilterStatus.Should().Contain("No current bounded");
        if (change is "failure" or "cancel") { viewer.LocalEventStatus.Should().Contain("held/unavailable"); }
        viewer.Close();
    }

    [WindowsFact]
    public async Task FilteringOneCategoryNeverTruncatesTheLoadedSnapshotWhenAnExactReasonChanges()
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        var now = f.Storage.Time.GetUtcNow();
        LocalEventType[] types = [LocalEventType.Queued, LocalEventType.Failed,
                LocalEventType.UserAttention, LocalEventType.MaintenanceAvailable];
        var rows = types.Select(type =>
        {
            var subject = Guid.NewGuid();
            var source = type == LocalEventType.UserAttention ? LocalEventSource.LocalVersionQuestion
                : type == LocalEventType.MaintenanceAvailable ? LocalEventSource.CachedMaintenance : LocalEventSource.LocalVersionQueue;
            return new LocalEvent(LocalEvent.Identity(source, f.Storage.Request.SessionId.Value, subject),
                1, source, type, f.Storage.Request.SessionId,
                source == LocalEventSource.CachedMaintenance ? null : new HostId<TaskIdentity>(subject),
                subject, 1, 1, 0, source == LocalEventSource.CachedMaintenance ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
                source == LocalEventSource.LocalVersionQueue ? SessionQueueEligibility.Ready : null, now, now.AddMinutes(30));
        }).ToArray();
        await using var broker = new LocalEventBroker(new FrozenSource(rows), f.State, f.Access, f.Storage.Time,
            f, NullLogger<LocalEventBroker>.Instance);
        var viewer = f.Viewer(broker);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        var loaded = viewer.LocalEvents.ToArray();
        loaded.Should().HaveCount(4);
        foreach (var category in viewer.LocalEventCategoryFilters)
        {
            foreach (var reason in viewer.LocalEventReasonFilters)
            {
                viewer.LocalEventCategoryChoice = category;
                viewer.LocalEventReasonChoice = reason;
                viewer.LocalEvents.Should().Equal(loaded.Where(new LocalEventFilter(category.Value, reason.Value).Matches));
            }
        }
        viewer.ClearLocalEventFilters();
        viewer.LocalEventCategoryFilter = LocalEventCategory.Attention;
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        await viewer.DismissLocalEventAsync();
        viewer.SelectedLocalEvent!.Reason.Should().Be(LocalEventReason.Dismissed);
        viewer.ClearLocalEventFilters();
        viewer.LocalEvents.Should().HaveCount(4);
        viewer.LocalEvents.Where(row => row.Event.Category != LocalEventCategory.Attention).Should()
            .Equal(loaded.Where(row => row.Event.Category != LocalEventCategory.Attention));
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.Close();
    }

    private sealed class FrozenSource(IReadOnlyList<LocalEvent> rows) : ILocalEventSource
    {
        public Task<IReadOnlyList<LocalEvent>> ReadAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            Task.FromResult(rows);

        public Task<T> WithCurrentAsync<T>(HostId<SessionIdentity> session, IReadOnlyList<LocalEvent> expected,
            Func<T> observation, CancellationToken cancellationToken) => Task.FromResult(observation());
    }
    [Theory]
    [InlineData("filter")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("close")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task PendingExactActionCannotRestoreSelectionOrDetailAfterItsPresentationWasRetired(string change)
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        var source = new PausedSource(f.Source());
        await using var broker = new LocalEventBroker(source, f.State, f.Access, f.Storage.Time,
            f, NullLogger<LocalEventBroker>.Instance);
        var viewer = f.Viewer(broker);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        source.Pause = true;
        var pending = viewer.ReviewLocalEventAsync();
        await source.Entered.Task;
        switch (change)
        {
            case "filter": viewer.LocalEventReasonFilter = LocalEventReason.Eligible; break;
            case "privacy": f.Access.CanInspect = false; f.Access.CanControl = false; break;
            case "revision": f.Access.ControlRevision++; break;
            case "close": viewer.Close(); break;
            case "failure": source.Failure = new IOException("test source unavailable"); break;
            case "cancel": source.Failure = new OperationCanceledException("test cancellation"); break;
        }
        if (change is not ("failure" or "cancel")) { viewer.SelectedLocalEvent.Should().BeNull(); }
        source.Release.TrySetResult();
        await pending;
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.LocalEvents.Should().BeEmpty();
        viewer.Detail.Should().BeEmpty();
        viewer.CanControlLocalEvent.Should().BeFalse();
        viewer.Close();
    }

    [WindowsFact]
    public async Task AdmissionRevisionAndWorkspaceChangesImmediatelyWithdrawFilteredRowsAndExactSelection()
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        await using var broker = f.Broker();
        var viewer = f.Viewer(broker);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        var row = viewer.LocalEvents.Single();
        viewer.SelectLocalEvent(row);
        f.Access.ControlRevision++;
        viewer.LocalEvents.Should().BeEmpty();
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.CanControlLocalEvent.Should().BeFalse();
        viewer.LocalEventStatus.Should().Contain("withdrawn");
        await viewer.RefreshWorkAsync();
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.LocalEvents.Should().ContainSingle();
        viewer.RevokeSessionList();
        viewer.LocalEvents.Should().BeEmpty();
        viewer.Close();
    }

    [WindowsFact]
    public async Task SourceQuestionRetirementCannotLeaveAnActionableFilteredRowOrLateExactDetail()
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        await using var broker = f.Broker();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access);
        var viewer = f.Viewer(broker, service);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.LocalEventCategoryFilter = LocalEventCategory.Attention;
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        var task = await service.ExecuteCommandAsync(new(SessionCommandOperation.TaskInspect, f.Storage.Request.SessionId.Value)
        { TaskId = f.Storage.Request.TaskId.Value }, RequestOrigin.LocalUi, () => true, f.Token);
        await service.CancelTaskAsync(WindowsSqliteTaskControlTests.Target(task.TaskDetails.Single()), RequestOrigin.LocalUi, () => true, f.Token);
        await viewer.ReviewLocalEventAsync();
        viewer.LocalEvents.Should().BeEmpty();
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.CanControlLocalEvent.Should().BeFalse();
        viewer.Detail.Should().BeEmpty();
        viewer.Status.Should().Contain("SourceUnavailable");
        viewer.Close();
    }

    private static void AssertEventActionsDisabled(SessionsViewModel viewer, SessionsWindow window)
    {
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.CanControlLocalEvent.Should().BeFalse();
        foreach (var name in new[] { "ReviewLocalEvent", "DismissLocalEvent", "DeferLocalEvent" })
        {
            window.FindControl<Button>(name)!.IsEnabled.Should().BeFalse();
        }
    }
}