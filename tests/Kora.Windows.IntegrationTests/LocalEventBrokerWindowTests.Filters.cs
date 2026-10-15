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

    [Theory]
    [MemberData(nameof(QueueReasons))]
    public async Task NativeQueueReasonChoicesHaveExactPositiveMatchesAndKeepQuestionMaintenanceAbsenceSeparate(SessionQueueEligibility eligibility)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            var now = f.Storage.Time.GetUtcNow();
            LocalEvent Create(LocalEventSource source, LocalEventType type, RequestOrigin origin, SessionQueueEligibility? queue)
            {
                var subject = Guid.NewGuid();
                return new(LocalEvent.Identity(source, f.Storage.Request.SessionId.Value, subject), 1, source, type,
                    f.Storage.Request.SessionId, source == LocalEventSource.CachedMaintenance ? null : new HostId<TaskIdentity>(subject),
                    subject, 1, 1, 0, origin, queue, now, now.AddMinutes(30));
            }
            var events = new[]
            {
                Create(LocalEventSource.LocalVersionQueue, LocalEventType.Blocked, RequestOrigin.LocalUi, eligibility),
                Create(LocalEventSource.LocalVersionQueue, LocalEventType.Blocked, RequestOrigin.ActivatedVoice, eligibility),
                Create(LocalEventSource.LocalVersionQuestion, LocalEventType.UserAttention, RequestOrigin.ActivatedVoice, null),
                Create(LocalEventSource.CachedMaintenance, LocalEventType.MaintenanceAvailable, RequestOrigin.HostSystem, null),
            };
            var source = new PausedSource(new FrozenSource(events));
            await using var broker = new LocalEventBroker(source, f.State, f.Access, f.Storage.Time,
                f, NullLogger<LocalEventBroker>.Instance);
            var viewer = f.Viewer(broker);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var loaded = viewer.LocalEvents.ToArray();
                loaded.Should().HaveCount(4);
                var reads = source.Reads;
                var originControl = window.FindControl<ComboBox>("LocalEventOriginFilter")!;
                var queueControl = window.FindControl<ComboBox>("LocalEventQueueReasonFilter")!;
                foreach (var origin in viewer.LocalEventOriginFilters)
                foreach (var queue in viewer.LocalEventQueueReasonFilters)
                {
                    originControl.SelectedItem = origin;
                    queueControl.SelectedItem = queue;
                    viewer.LocalEvents.Should().Equal(loaded.Where(new LocalEventFilter(Origin: origin.Value, QueueReason: queue.Value).Matches));
                    AssertEventActionsDisabled(viewer, window);
                }
                viewer.ClearLocalEventFilters();
                source.Reads.Should().Be(reads, "all native origin/queue choices filter loaded rows without another observation");
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.Exact, eligibility);
                viewer.LocalEvents.Should().HaveCount(2).And.OnlyContain(row => row.Event.QueueReason == eligibility);
                viewer.LocalEventOriginFilter = RequestOrigin.ActivatedVoice;
                viewer.LocalEvents.Should().ContainSingle().Which.Event.Source.Should().Be(LocalEventSource.LocalVersionQueue);
                viewer.LocalEventCategoryFilter = LocalEventCategory.Failure;
                viewer.LocalEventSourceFilter = LocalEventSource.LocalVersionQueue;
                viewer.LocalEventTypeFilter = LocalEventType.Blocked;
                viewer.LocalEventReasonFilter = viewer.LocalEvents.Single().Reason;
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                reads = source.Reads;
                viewer.LocalEventOriginFilter = RequestOrigin.LocalUi;
                viewer.LocalEvents.Should().BeEmpty("the other origin has a different delivery reason; all six predicates remain AND");
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                viewer.ClearLocalEventFilters();
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
                viewer.LocalEvents.Should().HaveCount(2).And.OnlyContain(row => row.Event.QueueReason == null
                    && row.Event.Source != LocalEventSource.LocalVersionQueue);
                viewer.LocalEventOriginFilter = RequestOrigin.HostSystem;
                viewer.LocalEvents.Should().ContainSingle().Which.Event.Type.Should().Be(LocalEventType.MaintenanceAvailable);
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                reads = source.Reads;
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.Exact, eligibility);
                viewer.LocalEvents.Should().BeEmpty();
                viewer.Detail.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("0 of 4").And.Contain("No matching loaded events");
                AssertEventActionsDisabled(viewer, window);
                source.Reads.Should().Be(reads);
            }
            finally { window.Close(); }
        });
    }

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
                var sources = viewer.LocalEventSourceFilters;
                var types = viewer.LocalEventTypeFilters;
                categories.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventCategory>().Select(value => (LocalEventCategory?)value)]);
                reasons.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventReason>().Select(value => (LocalEventReason?)value)]);
                sources.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventSource>().Select(value => (LocalEventSource?)value)]);
                types.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<LocalEventType>().Select(value => (LocalEventType?)value)]);
                categories[0].ToString().Should().Be("All");
                reasons[0].ToString().Should().Be("All");
                sources.Select(option => option.ToString()).Should()
                    .Equal(["All", .. Enum.GetNames<LocalEventSource>()]);
                types.Select(option => option.ToString()).Should()
                    .Equal(["All", .. Enum.GetNames<LocalEventType>()]);
                var categoryControl = window.FindControl<ComboBox>("LocalEventCategoryFilter")!;
                var reasonControl = window.FindControl<ComboBox>("LocalEventReasonFilter")!;
                var sourceControl = window.FindControl<ComboBox>("LocalEventSourceFilter")!;
                var typeControl = window.FindControl<ComboBox>("LocalEventTypeFilter")!;
                var originControl = window.FindControl<ComboBox>("LocalEventOriginFilter")!;
                var queueControl = window.FindControl<ComboBox>("LocalEventQueueReasonFilter")!;
                viewer.LocalEventOriginFilters.Select(option => option.Value).Should()
                    .Equal([null, .. Enum.GetValues<RequestOrigin>().Select(value => (RequestOrigin?)value)]);
                viewer.LocalEventOriginFilters.Select(option => option.ToString()).Should().Equal(["Any", .. Enum.GetNames<RequestOrigin>()]);
                viewer.LocalEventQueueReasonFilters.Select(option => option.Value).Should().Equal(
                    [new(), new(LocalEventQueueReasonMatch.None), .. Enum.GetValues<SessionQueueEligibility>()
                        .Select(value => new LocalEventQueueReasonFilter(LocalEventQueueReasonMatch.Exact, value))]);
                viewer.LocalEventQueueReasonFilters.Select(option => option.ToString()).Should()
                    .Equal(["Any", "No queue reason / not applicable", .. Enum.GetNames<SessionQueueEligibility>()]);
                ControlAutomationPeer.CreatePeerForElement(categoryControl)!.GetName().Should().Contain("authoritative category");
                ControlAutomationPeer.CreatePeerForElement(reasonControl)!.GetName().Should().Contain("exact delivery reason");
                foreach (var category in categories)
                {
                    foreach (var reason in reasons)
                    {
                        categoryControl.SelectedItem = category;
                        reasonControl.SelectedItem = reason;
                        foreach (var sourceOption in sources)
                        {
                            foreach (var typeOption in types)
                            {
                                sourceControl.SelectedItem = sourceOption;
                                typeControl.SelectedItem = typeOption;
                                viewer.LocalEvents.Should().Equal(rows.Where(new LocalEventFilter(
                                    category.Value, reason.Value, sourceOption.Value, typeOption.Value).Matches));
                                viewer.SelectedLocalEvent.Should().BeNull();
                                AssertEventActionsDisabled(viewer, window);
                                viewer.LocalEventStatus.Should().Be(snapshotStatus);
                            }
                        }
                    }
                }
                var questionRow = rows.Single();
                viewer.ClearLocalEventFilters();
                viewer.LocalEventSessionIdFilter = questionRow.Event.SessionId.Value.ToString("D");
                viewer.LocalEventTaskIdFilter = questionRow.Event.TaskId!.Value.Value.ToString("D");
                viewer.LocalEventSubjectIdFilter = questionRow.Event.SubjectId.ToString("D");
                viewer.LocalEvents.Should().Equal(rows);
                viewer.LocalEventSubjectIdFilter = "invalid";
                viewer.LocalEvents.Should().BeEmpty();
                viewer.ClearLocalEventFilters();
                viewer.LocalEventCategoryFilter = questionRow.Event.Category;
                viewer.LocalEventReasonFilter = questionRow.Reason;
                viewer.LocalEventSourceFilter = questionRow.Event.Source;
                viewer.LocalEventTypeFilter = questionRow.Event.Type;
                foreach (var origin in viewer.LocalEventOriginFilters)
                foreach (var queue in viewer.LocalEventQueueReasonFilters)
                {
                    originControl.SelectedItem = origin;
                    queueControl.SelectedItem = queue;
                    viewer.LocalEvents.Should().Equal(rows.Where(new LocalEventFilter(questionRow.Event.Category,
                        questionRow.Reason, questionRow.Event.Source, questionRow.Event.Type, origin.Value, queue.Value).Matches));
                    AssertEventActionsDisabled(viewer, window);
                    viewer.LocalEventStatus.Should().Be(snapshotStatus);
                    viewer.PendingQuestions.Single().Key.Should().Be(question.Key);
                }
                viewer.ClearLocalEventFilters();
                viewer.LocalEventOriginFilter = (RequestOrigin)99;
                viewer.LocalEventOriginChoice.Value.Should().Be((RequestOrigin)99);
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.LocalEvents.Should().BeEmpty();
                viewer.ClearLocalEventFilters();
                foreach (var invalid in new[] { new LocalEventQueueReasonFilter((LocalEventQueueReasonMatch)99),
                    new(LocalEventQueueReasonMatch.Exact), new(LocalEventQueueReasonMatch.Exact, (SessionQueueEligibility)99),
                    new(LocalEventQueueReasonMatch.None, SessionQueueEligibility.Ready) })
                {
                    viewer.LocalEventQueueReasonFilter = invalid;
                    viewer.LocalEventQueueReasonChoice.Value.Should().Be(invalid);
                    viewer.LocalEventQueueReasonChoice.ToString().Should().Be("Unknown");
                    viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                    viewer.LocalEvents.Should().BeEmpty();
                }
                viewer.ClearLocalEventFilters();
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
                viewer.LocalEventSourceFilter = (LocalEventSource)99;
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.LocalEventSourceChoice.Value.Should().Be((LocalEventSource)99);
                viewer.ClearLocalEventFilters();
                viewer.LocalEventTypeFilter = (LocalEventType)(-1);
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.LocalEventTypeChoice.Value.Should().Be((LocalEventType)(-1));
                viewer.ClearLocalEventFilters();
                viewer.LocalEventSourceFilter = LocalEventSource.CachedMaintenance;
                viewer.LocalEventTypeFilter = LocalEventType.UserAttention;
                viewer.LocalEventSourceFilter.Should().Be(LocalEventSource.CachedMaintenance);
                viewer.LocalEventTypeFilter.Should().Be(LocalEventType.UserAttention);
                viewer.LocalEventFilterStatus.Should().Contain("0 of 1").And.Contain("No matching loaded events");
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
                await viewer.ReviewLocalEventAsync();
                row = viewer.LocalEvents.Single();
                viewer.LocalEventSourceFilter = row.Event.Source;
                viewer.LocalEvents.Should().ContainSingle();
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(row);
                await viewer.ReviewLocalEventAsync();
                viewer.LocalEventTypeFilter = row.Event.Type;
                viewer.LocalEvents.Should().ContainSingle();
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(row);
                await viewer.ReviewLocalEventAsync();
                viewer.LocalEventOriginFilter = row.Event.Origin;
                viewer.LocalEvents.Should().ContainSingle();
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(row);
                await viewer.ReviewLocalEventAsync();
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
                viewer.LocalEvents.Should().ContainSingle("questions have no queue reason, not a successful queue receipt");
                viewer.Detail.Should().BeEmpty();
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
    [InlineData("source-filter")]
    [InlineData("type-filter")]
    [InlineData("origin-filter")]
    [InlineData("queue-filter")]
    [InlineData("session-id-filter")]
    [InlineData("task-id-filter")]
    [InlineData("subject-id-filter")]
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
        viewer.LocalEventSourceFilter = LocalEventSource.LocalVersionQuestion;
        viewer.LocalEventTypeFilter = LocalEventType.UserAttention;
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
            case "source-filter": viewer.LocalEventSourceFilter = null; break;
            case "type-filter": viewer.LocalEventTypeFilter = null; break;
            case "origin-filter": viewer.LocalEventOriginFilter = RequestOrigin.LocalUi; break;
            case "queue-filter": viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None); break;
            case "session-id-filter": viewer.LocalEventSessionIdFilter = f.Storage.Request.SessionId.Value.ToString("D"); break;
            case "task-id-filter": viewer.LocalEventTaskIdFilter = f.Storage.Request.TaskId.Value.ToString("D"); break;
            case "subject-id-filter": viewer.LocalEventSubjectIdFilter = Guid.NewGuid().ToString("D"); break;
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
                foreach (var source in viewer.LocalEventSourceFilters)
                {
                    foreach (var type in viewer.LocalEventTypeFilters)
                    {
                        viewer.LocalEventSourceChoice = source;
                        viewer.LocalEventTypeChoice = type;
                        viewer.LocalEvents.Should().Equal(loaded.Where(new LocalEventFilter(
                            category.Value, reason.Value, source.Value, type.Value).Matches));
                    }
                }
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
    [InlineData("source-filter")]
    [InlineData("type-filter")]
    [InlineData("origin-filter")]
    [InlineData("queue-filter")]
    [InlineData("session-id-filter")]
    [InlineData("task-id-filter")]
    [InlineData("subject-id-filter")]
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
        viewer.LocalEventSourceFilter = LocalEventSource.LocalVersionQuestion;
        viewer.LocalEventTypeFilter = LocalEventType.UserAttention;
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        await viewer.ReviewLocalEventAsync();
        viewer.Detail.Should().NotBeEmpty();
        source.Pause = true;
        var pending = viewer.ReviewLocalEventAsync();
        await source.Entered.Task;
        switch (change)
        {
            case "filter": viewer.LocalEventReasonFilter = LocalEventReason.Eligible; break;
            case "source-filter": viewer.LocalEventSourceFilter = null; break;
            case "type-filter": viewer.LocalEventTypeFilter = null; break;
            case "origin-filter": viewer.LocalEventOriginFilter = RequestOrigin.LocalUi; break;
            case "queue-filter": viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None); break;
            case "session-id-filter": viewer.LocalEventSessionIdFilter = f.Storage.Request.SessionId.Value.ToString("D"); break;
            case "task-id-filter": viewer.LocalEventTaskIdFilter = f.Storage.Request.TaskId.Value.ToString("D"); break;
            case "subject-id-filter": viewer.LocalEventSubjectIdFilter = Guid.NewGuid().ToString("D"); break;
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
        viewer.LocalEventSourceFilter = row.Event.Source;
        viewer.LocalEventTypeFilter = row.Event.Type;
        viewer.LocalEventOriginFilter = row.Event.Origin;
        viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
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
        viewer.LocalEventSourceFilter = LocalEventSource.LocalVersionQuestion;
        viewer.LocalEventTypeFilter = LocalEventType.UserAttention;
        viewer.LocalEventOriginFilter = RequestOrigin.LocalUi;
        viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
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
        viewer.LocalEventSessionIdFilter = row.Event.SessionId.Value.ToString("D");
        viewer.LocalEventTaskIdFilter = row.Event.TaskId!.Value.Value.ToString("D");
        viewer.LocalEventSubjectIdFilter = row.Event.SubjectId.ToString("D");
        viewer.LocalEventSourceFilter = row.Event.Source;
        viewer.LocalEventTypeFilter = row.Event.Type;
        viewer.LocalEventOriginFilter = row.Event.Origin;
        viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
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
    public async Task ExactFilterLabelsAndKeyboardNavigationRequireSeparateVisibleEventSelectionWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
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
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var reads = source.Reads;
                var category = window.FindControl<ComboBox>("LocalEventCategoryFilter")!;
                var reason = window.FindControl<ComboBox>("LocalEventReasonFilter")!;
                var sourceFilter = window.FindControl<ComboBox>("LocalEventSourceFilter")!;
                var type = window.FindControl<ComboBox>("LocalEventTypeFilter")!;
                var origin = window.FindControl<ComboBox>("LocalEventOriginFilter")!;
                var queue = window.FindControl<ComboBox>("LocalEventQueueReasonFilter")!;
                var sessionId = window.FindControl<TextBox>("LocalEventSessionIdFilter")!;
                var taskId = window.FindControl<TextBox>("LocalEventTaskIdFilter")!;
                var subjectId = window.FindControl<TextBox>("LocalEventSubjectIdFilter")!;
                var clear = window.FindControl<Button>("ClearLocalEventFilters")!;
                ControlAutomationPeer.CreatePeerForElement(sourceFilter)!.GetName().Should().Be(
                    "Filter loaded local events by exact authoritative source or All; combines with category, reason and type and clears exact event selection");
                ControlAutomationPeer.CreatePeerForElement(type)!.GetName().Should().Be(
                    "Filter loaded local events by exact authoritative type or All; combines with category, reason and source and clears exact event selection");
                ControlAutomationPeer.CreatePeerForElement(clear)!.GetName().Should().Be(
                    "Show all already-loaded local events and clear exact selection; no source read or quiet change");
                ControlAutomationPeer.CreatePeerForElement(origin)!.GetName().Should().Be(
                    "Filter loaded local events by exact request origin or Any; combines with all event filters and clears exact event selection");
                ControlAutomationPeer.CreatePeerForElement(queue)!.GetName().Should().Be(
                    "Filter loaded local events by Any, No queue reason / not applicable, or exact queue eligibility reason; combines with all event filters and clears exact event selection; absence is not success or authority");
                category.Focus().Should().BeTrue();
                foreach (var field in new[] { sessionId, taskId, subjectId })
                {
                    ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should()
                        .Contain("complete lowercase D GUID").And.Contain("correlation only, never retargets or authorizes");
                }
                foreach (var next in new Control[] { reason, sourceFilter, type, origin, queue, sessionId, taskId, subjectId, clear })
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    window.FocusManager!.GetFocusedElement().Should().BeSameAs(next);
                }
                foreach (var previous in new Control[] { subjectId, taskId, sessionId, queue, origin, type, sourceFilter, reason, category })
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                    window.FocusManager!.GetFocusedElement().Should().BeSameAs(previous);
                }
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                reads = source.Reads;
                sourceFilter.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventSourceFilter.Should().Be(LocalEventSource.LocalVersionQueue);
                viewer.Detail.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                viewer.LocalEventSourceFilter.Should().BeNull();
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                type.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventTypeFilter.Should().Be(LocalEventType.Queued);
                AssertEventActionsDisabled(viewer, window);
                viewer.ClearLocalEventFilters();
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                origin.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventOriginFilter.Should().Be(RequestOrigin.LocalUi);
                AssertEventActionsDisabled(viewer, window);
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                queue.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventQueueReasonFilter.Should().Be(new LocalEventQueueReasonFilter(LocalEventQueueReasonMatch.None));
                viewer.LocalEvents.Should().ContainSingle();
                AssertEventActionsDisabled(viewer, window);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventQueueReasonFilter.Should().Be(new LocalEventQueueReasonFilter(LocalEventQueueReasonMatch.Exact, SessionQueueEligibility.Ready));
                viewer.LocalEvents.Should().BeEmpty();
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                viewer.LocalEvents.Should().ContainSingle();
                clear.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                viewer.LocalEventSourceFilter.Should().BeNull();
                viewer.LocalEventTypeFilter.Should().BeNull();
                viewer.LocalEventOriginFilter.Should().BeNull();
                viewer.LocalEventQueueReasonFilter.Should().Be(default(LocalEventQueueReasonFilter));
                AssertEventActionsDisabled(viewer, window);
                var events = window.FindControl<ListBox>("EventRecords")!;
                window.UpdateLayout();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                window.FocusManager!.GetFocusedElement().Should().BeOfType<ListBoxItem>();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                events.SelectedItem.Should().BeSameAs(viewer.LocalEvents.Single());
                viewer.SelectedLocalEvent.Should().BeSameAs(viewer.LocalEvents.Single());
                viewer.CanControlLocalEvent.Should().BeTrue();
                source.Reads.Should().Be(reads, "keyboard filtering and explicit selection do not observe a source");
            }
            finally { window.Close(); }
        });
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
        viewer.LocalEventSourceFilter = LocalEventSource.CachedMaintenance;
        viewer.LocalEventTypeFilter = LocalEventType.MaintenanceAvailable;
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