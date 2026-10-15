using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalEventBrokerWindowTests
{
    [WindowsFact]
    public async Task NativeIdentityFieldsComposeOverLoadedRowsWithoutReadsOrRetargetingAndRetireDetails()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            var now = f.Storage.Time.GetUtcNow();
            var task = new HostId<TaskIdentity>(Guid.NewGuid());
            var subject = Guid.NewGuid();
            var maintenanceSubject = Guid.NewGuid();
            var session = f.Storage.Request.SessionId;
            var events = new[]
            {
                new LocalEvent(LocalEvent.Identity(LocalEventSource.LocalVersionQueue, session.Value, subject), 1,
                    LocalEventSource.LocalVersionQueue, LocalEventType.Blocked, session, task, subject, 1, 1, 0,
                    RequestOrigin.LocalUi, SessionQueueEligibility.GlobalCapacity, now, now.AddMinutes(30)),
                new LocalEvent(LocalEvent.Identity(LocalEventSource.CachedMaintenance, session.Value, maintenanceSubject), 1,
                    LocalEventSource.CachedMaintenance, LocalEventType.MaintenanceAvailable, session, null, maintenanceSubject, 1, 1, 0,
                    RequestOrigin.HostSystem, null, now, now.AddMinutes(30)),
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
                loaded.Should().HaveCount(2);
                window.UpdateLayout();
                window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should()
                    .Contain("Session ID " + session.Value.ToString("D"))
                    .And.Contain("Task ID " + task.Value.ToString("D"))
                    .And.Contain("Subject ID " + subject.ToString("D"));
                var selectedSession = viewer.SelectedSessionRecord;
                var reads = source.Reads;
                var sessionControl = window.FindControl<TextBox>("LocalEventSessionIdFilter")!;
                var taskControl = window.FindControl<TextBox>("LocalEventTaskIdFilter")!;
                var subjectControl = window.FindControl<TextBox>("LocalEventSubjectIdFilter")!;
                sessionControl.Text = session.Value.ToString("D");
                viewer.LocalEventSessionIdFilter.Should().Be(sessionControl.Text);
                viewer.LocalEvents.Should().Equal(loaded);
                taskControl.Text = task.Value.ToString("D");
                viewer.LocalEvents.Should().ContainSingle().Which.Event.TaskId.Should().Be(task);
                subjectControl.Text = subject.ToString("D");
                viewer.LocalEvents.Should().ContainSingle();
                viewer.LocalEventCategoryFilter = LocalEventCategory.Failure;
                viewer.LocalEventReasonFilter = viewer.LocalEvents.Single().Reason;
                viewer.LocalEventSourceFilter = LocalEventSource.LocalVersionQueue;
                viewer.LocalEventTypeFilter = LocalEventType.Blocked;
                viewer.LocalEventOriginFilter = RequestOrigin.LocalUi;
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.Exact, SessionQueueEligibility.GlobalCapacity);
                source.Reads.Should().Be(reads);
                viewer.LocalEventReasonFilter = null;
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                viewer.Detail.Should().NotBeEmpty();
                reads = source.Reads;
                subjectControl.Text = Guid.NewGuid().ToString("D");
                viewer.Detail.Should().BeEmpty();
                viewer.LocalEvents.Should().BeEmpty();
                AssertEventActionsDisabled(viewer, window);
                subjectControl.Text = subject.ToString("D");
                viewer.SelectedLocalEvent.Should().BeNull();
                foreach (var field in new[] { sessionControl, taskControl, subjectControl })
                {
                    var canonical = field.Text;
                    foreach (var invalid in new[] { " ", canonical![..^1], canonical.ToUpperInvariant(), " " + canonical })
                    {
                        field.Text = invalid;
                        viewer.LocalEvents.Should().BeEmpty();
                        viewer.LocalEventFilterStatus.Should().Contain("invalid exact ID");
                        AssertEventActionsDisabled(viewer, window);
                    }
                    field.Text = canonical;
                }
                viewer.ClearLocalEventFilters();
                sessionControl.Text.Should().BeEmpty();
                taskControl.Text.Should().BeEmpty();
                subjectControl.Text.Should().BeEmpty();
                viewer.LocalEvents.Should().HaveCount(2);
                taskControl.Text = maintenanceSubject.ToString("D");
                viewer.LocalEvents.Should().BeEmpty("subject identity cannot stand in for an absent task");
                taskControl.Text = "";
                subjectControl.Text = maintenanceSubject.ToString("D");
                viewer.LocalEvents.Should().ContainSingle().Which.Event.TaskId.Should().BeNull();
                viewer.CanControlLocalEvent.Should().BeFalse("correlation cannot select or authorize navigation");
                sessionControl.Text = Guid.NewGuid().ToString("D");
                viewer.LocalEvents.Should().BeEmpty();
                viewer.SelectedSessionRecord.Should().Be(selectedSession);
                source.Reads.Should().Be(reads);
                f.Access.CanInspect = false;
                f.Access.CanControl = false;
                viewer.LocalEvents.Should().BeEmpty();
                viewer.Detail.Should().BeEmpty();
                viewer.RevokeSessionList();
                sessionControl.IsEnabled.Should().BeFalse();
                taskControl.IsEnabled.Should().BeFalse();
                subjectControl.IsEnabled.Should().BeFalse();
                var prior = (viewer.LocalEventSessionIdFilter, viewer.LocalEventTaskIdFilter, viewer.LocalEventSubjectIdFilter);
                viewer.LocalEventSessionIdFilter = subject.ToString("D");
                viewer.LocalEventTaskIdFilter = subject.ToString("D");
                viewer.LocalEventSubjectIdFilter = subject.ToString("D");
                (viewer.LocalEventSessionIdFilter, viewer.LocalEventTaskIdFilter, viewer.LocalEventSubjectIdFilter).Should().Be(prior);
            }
            finally { window.Close(); }
        });
    }
}
