using System.Globalization;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalEventBrokerWindowTests
{
    private static (string Name, Action<string> Set, Func<string> Get, string Exact)[] RangeFields(
        SessionsViewModel viewer, LocalEvent row) =>
    [
        ("LocalEventMinimumRevisionFilter", value => viewer.LocalEventMinimumRevisionFilter = value,
            () => viewer.LocalEventMinimumRevisionFilter, row.Revision.ToString(CultureInfo.InvariantCulture)),
        ("LocalEventMaximumRevisionFilter", value => viewer.LocalEventMaximumRevisionFilter = value,
            () => viewer.LocalEventMaximumRevisionFilter, row.Revision.ToString(CultureInfo.InvariantCulture)),
        ("LocalEventMinimumGenerationFilter", value => viewer.LocalEventMinimumGenerationFilter = value,
            () => viewer.LocalEventMinimumGenerationFilter, row.Generation.ToString(CultureInfo.InvariantCulture)),
        ("LocalEventMaximumGenerationFilter", value => viewer.LocalEventMaximumGenerationFilter = value,
            () => viewer.LocalEventMaximumGenerationFilter, row.Generation.ToString(CultureInfo.InvariantCulture)),
        ("LocalEventObservedFromUtcFilter", value => viewer.LocalEventObservedFromUtcFilter = value,
            () => viewer.LocalEventObservedFromUtcFilter, row.ObservedAt.ToString("O", CultureInfo.InvariantCulture)),
        ("LocalEventObservedUntilUtcFilter", value => viewer.LocalEventObservedUntilUtcFilter = value,
            () => viewer.LocalEventObservedUntilUtcFilter, row.ObservedAt.ToString("O", CultureInfo.InvariantCulture)),
        ("LocalEventExpiresFromUtcFilter", value => viewer.LocalEventExpiresFromUtcFilter = value,
            () => viewer.LocalEventExpiresFromUtcFilter, row.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
        ("LocalEventExpiresUntilUtcFilter", value => viewer.LocalEventExpiresUntilUtcFilter = value,
            () => viewer.LocalEventExpiresUntilUtcFilter, row.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
    ];

    [WindowsFact]
    public async Task NativeInclusiveRangesComposeWithoutReadsAndRetireSelectionDetailAndActionsEvenWhenStillMatching()
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
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                var row = viewer.LocalEvents.Single();
                var selectedSession = viewer.SelectedSessionRecord;
                var fields = RangeFields(viewer, row.Event);
                viewer.LocalEventSessionIdFilter = row.Event.SessionId.Value.ToString("D");
                viewer.LocalEventTaskIdFilter = row.Event.TaskId!.Value.Value.ToString("D");
                viewer.LocalEventSubjectIdFilter = row.Event.SubjectId.ToString("D");
                viewer.LocalEventCategoryFilter = row.Event.Category;
                viewer.LocalEventSourceFilter = row.Event.Source;
                viewer.LocalEventTypeFilter = row.Event.Type;
                viewer.LocalEventOriginFilter = row.Event.Origin;
                viewer.LocalEventQueueReasonFilter = new(LocalEventQueueReasonMatch.None);
                foreach (var (name, set, get, exact) in fields)
                {
                    viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                    await viewer.ReviewLocalEventAsync();
                    viewer.Detail.Should().NotBeEmpty();
                    var reads = source.Reads;
                    var control = window.FindControl<TextBox>(name)!;
                    control.Focusable.Should().BeTrue();
                    control.Focus();
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should()
                        .Contain("inclusive").And.Contain("empty Any").And.Contain("AND with all filters")
                        .And.Contain("no read").And.Contain("retires selection and details").And.Contain("never authority");
                    control.Text = exact;
                    get().Should().Be(exact);
                    viewer.LocalEvents.Should().ContainSingle();
                    viewer.Detail.Should().BeEmpty();
                    AssertEventActionsDisabled(viewer, window);
                    foreach (var invalid in new[] { " ", "0", " " + exact })
                    {
                        control.Text = invalid;
                        viewer.LocalEvents.Should().BeEmpty();
                        viewer.LocalEventFilterStatus.Should().Contain("invalid range filter");
                        AssertEventActionsDisabled(viewer, window);
                    }
                    set(exact);
                    control.Text.Should().Be(exact);
                    viewer.LocalEvents.Should().ContainSingle();
                    source.Reads.Should().Be(reads);
                }
                var noRead = source.Reads;
                viewer.LocalEventReasonFilter = viewer.LocalEvents.Single().Reason;
                viewer.LocalEventMinimumRevisionFilter = long.MaxValue.ToString(CultureInfo.InvariantCulture);
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventFilterStatus.Should().Contain("invalid range filter", "the maximum is still the original revision");
                viewer.LocalEventMaximumRevisionFilter = "";
                viewer.LocalEventFilterStatus.Should().Contain("No matching loaded events");
                viewer.SelectedSessionRecord.Should().Be(selectedSession);
                viewer.ClearLocalEventFilters();
                foreach (var (name, _, get, _) in fields)
                {
                    get().Should().BeEmpty();
                    window.FindControl<TextBox>(name)!.Text.Should().BeEmpty();
                }
                viewer.LocalEvents.Should().ContainSingle();
                source.Reads.Should().Be(noRead);
                viewer.LocalEventFilterStatus.Should().Contain("1 of 1");
                foreach (var (_, set, _, exact) in fields) { set(exact); }
                f.Access.ControlRevision++;
                viewer.LocalEvents.Should().BeEmpty();
                viewer.SelectedLocalEvent.Should().BeNull();
                viewer.Detail.Should().BeEmpty();
                f.Access.CanInspect = false;
                f.Access.CanControl = false;
                viewer.RevokeSessionList();
                foreach (var (name, set, get, _) in fields)
                {
                    window.FindControl<TextBox>(name)!.IsEnabled.Should().BeFalse();
                    var prior = get();
                    set("different");
                    get().Should().Be(prior);
                }
                AssertEventActionsDisabled(viewer, window);
                source.Reads.Should().Be(noRead);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryRangeEditFencesAnInFlightSnapshotOrExactReview(bool review)
    {
        for (var index = 0; index < 8; index++)
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
            var row = viewer.LocalEvents.Single();
            source.Pause = true;
            var pending = review ? viewer.ReviewLocalEventAsync() : viewer.RefreshWorkAsync();
            await source.Entered.Task;
            var field = RangeFields(viewer, row.Event)[index];
            field.Set(field.Exact);
            viewer.SelectedLocalEvent.Should().BeNull();
            viewer.Detail.Should().BeEmpty();
            viewer.CanControlLocalEvent.Should().BeFalse();
            source.Release.TrySetResult();
            await pending;
            viewer.LocalEvents.Should().BeEmpty();
            viewer.SelectedLocalEvent.Should().BeNull();
            viewer.Detail.Should().BeEmpty();
            viewer.CanControlLocalEvent.Should().BeFalse();
            viewer.Close();
        }
    }
}
