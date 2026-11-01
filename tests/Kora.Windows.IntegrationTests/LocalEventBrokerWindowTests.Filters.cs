using System.Diagnostics;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

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
    public static TheoryData<SessionQueueEligibility> QueueReasons => new(Enum.GetValues<SessionQueueEligibility>());

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
                await viewer.ReviewLocalEventAsync();
                row = viewer.LocalEvents.Single();
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
    [InlineData("clear-filters")]
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
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        await viewer.ReviewLocalEventAsync();
        source.Pause = true;
        var pending = viewer.RefreshWorkAsync();
        await source.Entered.Task;
        viewer.SelectedLocalEvent.Should().BeNull("refresh revokes at entry, not only after a successful read");
        viewer.CanControlLocalEvent.Should().BeFalse();
        viewer.Detail.Should().BeEmpty();
        switch (change)
        {
            case "filter": viewer.LocalEventCategoryFilter = LocalEventCategory.Attention; break;
            case "profile-away-back":
                break;
            case "target-away-back":
                break;
            case "clear-filters": viewer.ClearLocalEventFilters(); break;
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
        var types = Enum.GetValues<LocalEventType>();
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
        loaded.Should().HaveCount(LocalEventSnapshot.MaximumVisible);
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
        viewer.LocalEvents.Should().HaveCount(LocalEventSnapshot.MaximumVisible);
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
    [InlineData("clear-filters")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("workspace")]
    [InlineData("session")]
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
        await viewer.ReviewLocalEventAsync();
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        await viewer.ReviewLocalEventAsync();
        var suppression = f.State.Load();
        var questions = await f.Storage.Store.ReadQuestionPageAsync(f.Storage.Request.SessionId, null, 25, f.Token);
        var retention = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token);
        var history = await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token);
        var tasks = f.Storage.Count("host_tasks");
        var audits = f.Storage.Count("security_audit_events");
        source.Pause = true;
        var pending = viewer.ReviewLocalEventAsync();
        await source.Entered.Task;
        viewer.Detail.Should().BeEmpty("the admitted notice action owns its detail pane before awaiting the source");
        switch (change)
        {
            case "filter": viewer.LocalEventReasonFilter = LocalEventReason.Eligible; break;
            case "profile-away-back":
                break;
            case "target-away-back":
                break;
            case "clear-filters": viewer.ClearLocalEventFilters(); break;
            case "privacy": f.Access.CanInspect = false; f.Access.CanControl = false; break;
            case "revision": f.Access.ControlRevision++; break;
            case "workspace": viewer.RevokeSessionList(); break;
            case "session": viewer.HistorySessionId = Guid.NewGuid().ToString("D"); break;
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
        viewer.CanAnswerConversationNotice.Should().BeFalse();
        viewer.Status.Should().Contain("unavailable");
        f.State.Load().Should().BeEquivalentTo(suppression);
        f.Storage.Count("host_tasks").Should().Be(tasks);
        f.Storage.Count("security_audit_events").Should().Be(audits);
        (await f.Storage.Store.ReadQuestionPageAsync(f.Storage.Request.SessionId, null, 25, f.Token))
            .Should().BeEquivalentTo(questions);
        (await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token)).Should().Be(retention);
        (await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token))
            .Should().BeEquivalentTo(history);
        if (change is "clear-filters" or "failure" or "cancel")
        {
            var reads = source.Reads;
            await viewer.ReviewLocalEventAsync();
            source.Reads.Should().Be(reads, "retired presentation cannot initiate another exact source read");
            viewer.LocalEvents.Should().BeEmpty();
            source.Pause = false;
            source.Failure = null;
            await viewer.RefreshWorkAsync();
            viewer.LocalEvents.Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.PresentedNoReplay);
            viewer.SelectedLocalEvent.Should().BeNull();
            viewer.CanControlLocalEvent.Should().BeFalse();
            viewer.Detail.Should().BeEmpty();
        }
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
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        f.Access.ControlRevision++;
        viewer.Detail.Should().BeEmpty();
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

    [Theory]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("workspace")]
    [InlineData("session")]
    [InlineData("surface")]
    [InlineData("close")]
    public async Task PublishedFilteredEventDetailIsUnavailableImmediatelyAfterPresentationOwnershipChanges(string change)
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
        var source = new PausedSource(f.Source());
        await using var broker = new LocalEventBroker(source, f.State, f.Access, f.Storage.Time,
            f, NullLogger<LocalEventBroker>.Instance);
        var viewer = f.Viewer(broker);
        viewer.SetRoutineQuietSurfaceOpen(true);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        var row = viewer.LocalEvents.Single();
        viewer.SelectLocalEvent(row);
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        var reads = source.Reads;
        switch (change)
        {
            case "privacy": f.Access.CanInspect = false; f.Access.CanControl = false; break;
            case "revision": f.Access.ControlRevision++; break;
            case "workspace": viewer.RevokeSessionList(); break;
            case "session": viewer.HistorySessionId = Guid.NewGuid().ToString("D"); break;
            case "surface": viewer.SetRoutineQuietSurfaceOpen(false); break;
            case "close": viewer.Close(); break;
        }
        viewer.LocalEvents.Should().BeEmpty();
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.Detail.Should().BeEmpty();
        viewer.CanControlLocalEvent.Should().BeFalse();
        source.Reads.Should().Be(reads);
        viewer.Close();
    }

    [WindowsFact]
    public async Task WorkDetailReplacesEventDetailWithoutInheritingItsSnapshotOrFilterLifetime()
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
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.Task is not null));
        viewer.SelectedLocalEvent.Should().BeNull();
        viewer.Detail.Should().BeEmpty();
        await viewer.InspectWorkAsync();
        var workDetail = viewer.Detail;
        workDetail.Should().NotBeEmpty();
        var reads = source.Reads;
        viewer.ClearLocalEventFilters();
        viewer.Detail.Should().Be(workDetail, "non-event detail is independent of the retired event snapshot");
        source.Reads.Should().Be(reads);
        viewer.CanControlLocalEvent.Should().BeFalse();
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