using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalEventBrokerWindowTests
{
    private static readonly string[] QueueProfilePresenceLabels = ["Any", "Absent retained queue profile"];
    private static readonly string[] QueueTargetAnyLabels = ["Any"];

    public static TheoryData<SessionQueueReadProfile, string?> RetainedQueueMetadata => new(
        Enum.GetValues<SessionQueueReadProfile>().Where(value => value != SessionQueueReadProfile.Capability)
            .Select(value => (value, (string?)null))
            .Concat(ReadOnlyCapabilityCatalog.Descriptors.Select(value => (SessionQueueReadProfile.Capability, (string?)value.Id))));

    [Theory]
    [MemberData(nameof(RetainedQueueMetadata))]
    public async Task NativeHistoricalCatalogueChoicesAreAccessiblePassiveAndComposeWithoutAuthorityOrSourceEffects(
        SessionQueueReadProfile profile, string? targetId)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            var now = f.Storage.Time.GetUtcNow();
            LocalEvent Create(LocalEventSource source, LocalEventType type, SessionQueueReadProfile? read = null,
                SessionQueueCapabilityTarget? target = null)
            {
                var subject = Guid.NewGuid();
                return new(LocalEvent.Identity(source, f.Storage.Request.SessionId.Value, subject), 1, source, type,
                    f.Storage.Request.SessionId, source == LocalEventSource.CachedMaintenance ? null : new HostId<TaskIdentity>(subject),
                    subject, 1, 1, 0, source == LocalEventSource.CachedMaintenance ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
                    source == LocalEventSource.LocalVersionQueue ? SessionQueueEligibility.Ready : null, now, now.AddMinutes(30))
                { QueueProfile = read, QueueTarget = target };
            }
            var target = targetId is null ? null : new SessionQueueCapabilityTarget(targetId);
            var events = new[]
            {
                Create(LocalEventSource.LocalVersionQueue, LocalEventType.Queued, profile, target),
                Create(LocalEventSource.LocalVersionQueue, LocalEventType.Queued),
                Create(LocalEventSource.LocalVersionQuestion, LocalEventType.UserAttention),
                Create(LocalEventSource.CachedMaintenance, LocalEventType.MaintenanceAvailable),
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
                var rows = viewer.LocalEvents.ToArray();
                rows.Should().HaveCount(4);
                var session = viewer.SelectedSessionRecord;
                var work = viewer.WorkRecords.ToArray();
                var questions = viewer.PendingQuestions.ToArray();
                var status = viewer.LocalEventStatus;
                var reads = source.Reads;
                var database = File.ReadAllBytes(f.Storage.DatabasePath);
                var preferences = File.ReadAllBytes(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json"));
                var profileControl = window.FindControl<ComboBox>("LocalEventQueueProfileFilter")!;
                var targetControl = window.FindControl<ComboBox>("LocalEventQueueTargetFilter")!;
                foreach (var control in new[] { profileControl, targetControl })
                {
                    control.Focus().Should().BeTrue();
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should()
                        .Contain("AND with all filters").And.Contain("no read").And.Contain("retires selection and details")
                        .And.Contain("historical metadata never authority");
                }
                viewer.LocalEventQueueProfileFilters.Select(value => value.ToString()).Should()
                    .Equal(QueueProfilePresenceLabels.Concat(
                        Enum.GetValues<SessionQueueReadProfile>().Select(SessionQueueReadProfiles.CapabilityId)));
                viewer.LocalEventQueueTargetFilters.Select(value => value.ToString()).Should()
                    .Equal(QueueTargetAnyLabels.Concat(ReadOnlyCapabilityCatalog.Descriptors.Select(value => value.Id)));
                foreach (var profileChoice in viewer.LocalEventQueueProfileFilters)
                foreach (var targetChoice in viewer.LocalEventQueueTargetFilters)
                {
                    profileControl.SelectedItem = profileChoice;
                    targetControl.SelectedItem = targetChoice;
                    viewer.LocalEvents.Should().Equal(rows.Where(new LocalEventFilter(
                        QueueProfile: profileChoice.Value, QueueTarget: targetChoice.Value).Matches));
                    AssertEventActionsDisabled(viewer, window);
                }
                viewer.ClearLocalEventFilters();
                profileControl.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                viewer.LocalEventQueueProfileFilter.Should().Be(new LocalEventQueueProfileFilter(LocalEventQueueProfileMatch.Absent));
                viewer.LocalEvents.Should().HaveCount(3).And.NotContain(row => row.Event.QueueProfile != null);
                viewer.LocalEventQueueProfileFilter = new(LocalEventQueueProfileMatch.Exact, profile);
                viewer.LocalEvents.Should().ContainSingle().Which.Event.Should().Be(events[0]);
                viewer.LocalEventQueueTargetFilter = target;
                viewer.LocalEvents.Should().ContainSingle();
                viewer.LocalEventCategoryFilter = events[0].Category;
                viewer.LocalEventReasonFilter = viewer.LocalEvents.Single().Reason;
                viewer.LocalEventSourceFilter = events[0].Source;
                viewer.LocalEventTypeFilter = events[0].Type;
                viewer.LocalEventOriginFilter = events[0].Origin;
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.Exact, SessionQueueEligibility.Ready);
                viewer.LocalEventSessionIdFilter = events[0].SessionId.Value.ToString("D");
                viewer.LocalEventTaskIdFilter = events[0].TaskId!.Value.Value.ToString("D");
                viewer.LocalEventSubjectIdFilter = events[0].SubjectId.ToString("D");
                viewer.LocalEventMinimumRevisionFilter = "1";
                viewer.LocalEventMaximumGenerationFilter = "1";
                viewer.LocalEvents.Should().ContainSingle();
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                // A matching edit must still retire selection without executing any action.
                viewer.LocalEventQueueProfileFilter = default;
                AssertEventActionsDisabled(viewer, window);
                viewer.LocalEventQueueProfileFilter = new(LocalEventQueueProfileMatch.Absent);
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("0 of 4").And.Contain("No matching loaded events");
                viewer.LocalEventQueueProfileFilter = new(LocalEventQueueProfileMatch.Exact, (SessionQueueReadProfile)99);
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("Unknown");
                viewer.LocalEventQueueTargetFilter = new("CAPABILITIES.LIST");
                viewer.LocalEvents.Should().BeEmpty();
                viewer.ClearLocalEventFilters();
                viewer.LocalEventQueueTargetFilter = new(ReadOnlyCapabilityCatalog.Version);
                viewer.LocalEvents.Should().HaveCount(string.Equals(targetId, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal) ? 1 : 0,
                    "the target ID is never inferred from profile, including the fixed runtime target");
                viewer.ClearLocalEventFilters();
                viewer.LocalEvents.Should().Equal(rows);
                profileControl.SelectedItem.Should().Be(viewer.LocalEventQueueProfileFilters[0]);
                targetControl.SelectedItem.Should().Be(viewer.LocalEventQueueTargetFilters[0]);
                source.Reads.Should().Be(reads);
                viewer.SelectedSessionRecord.Should().Be(session);
                viewer.WorkRecords.Should().Equal(work);
                viewer.PendingQuestions.Should().Equal(questions);
                viewer.LocalEventStatus.Should().Be(status);
                File.ReadAllBytes(f.Storage.DatabasePath).Should().Equal(database);
                File.ReadAllBytes(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json")).Should().Equal(preferences);
                f.Access.ControlRevision++;
                viewer.LocalEvents.Should().BeEmpty();
                viewer.SelectedLocalEvent.Should().BeNull();
                f.Access.CanInspect = false;
                f.Access.CanControl = false;
                viewer.RevokeSessionList();
                profileControl.IsEnabled.Should().BeFalse();
                targetControl.IsEnabled.Should().BeFalse();
                viewer.LocalEventQueueProfileFilter = new(LocalEventQueueProfileMatch.Absent);
                viewer.LocalEventQueueTargetFilter = target;
                viewer.LocalEventQueueProfileFilter.Should().Be(default(LocalEventQueueProfileFilter));
                viewer.LocalEventQueueTargetFilter.Should().BeNull();
                AssertEventActionsDisabled(viewer, window);
                source.Reads.Should().Be(reads);
            }
            finally { window.Close(); }
        });
    }
}
