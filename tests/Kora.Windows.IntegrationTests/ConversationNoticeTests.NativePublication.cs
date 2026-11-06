using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class ConversationNoticeTests
{
    [Theory]
    [InlineData("visible", nameof(SessionsViewModel.WorkRecords))]
    [InlineData("hidden", nameof(SessionsViewModel.WorkRecords))]
    [InlineData("reopened", nameof(SessionsViewModel.WorkRecords))]
    [InlineData("visible", nameof(SessionsViewModel.LocalEvents))]
    [InlineData("hidden", nameof(SessionsViewModel.LocalEvents))]
    [InlineData("reopened", nameof(SessionsViewModel.LocalEvents))]
    public async Task WorkPublicationCannotRenewRetiredNativeNoticeDelivery(string presentation, string notification)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await using var broker = f.Broker();
            var viewer = f.Viewer(broker);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var request = await f.Submit(ModelTurnOutcome.Succeeded);
                var originalSession = viewer.SelectedSessionRecord;
                viewer.ComposerBody = "independent unsent input";
                var beforeRetention = await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token);
                var beforeTasks = f.Storage.Count("host_tasks");
                var beforeMessages = f.Storage.Count("conversation_messages");
                var beforeAudits = f.Storage.Count("security_audit_events");
                var store = new LocalEventStateStore(f.Storage.Paths);
                var beforeState = store.Load();
                var reachedPublication = false;
                viewer.PropertyChanged += (_, args) =>
                {
                    if (reachedPublication || !string.Equals(args.PropertyName, notification, StringComparison.Ordinal)) { return; }
                    reachedPublication = true;
                    if (presentation is "visible") { return; }
                    window.Hide();
                    if (presentation is "reopened") { window.Show(); }
                };

                await viewer.RefreshWorkAsync();

                reachedPublication.Should().BeTrue();
                viewer.SelectedSessionRecord.Should().Be(originalSession);
                viewer.WorkRecords.Should().Contain(row => row.TaskId == request.TaskId.Value);
                viewer.ComposerBody.Should().Be("independent unsent input");
                viewer.CanRefreshWork.Should().BeTrue();
                broker.IsAvailable.Should().BeTrue("retired presentation is not corrupt source or storage");
                if (presentation is not "visible")
                {
                    store.Load().Should().BeEquivalentTo(beforeState, "an undisplayed completion cannot consume suppression or fatigue");
                    viewer.LocalEvents.Should().BeEmpty();
                    viewer.SelectedLocalEvent.Should().BeNull();
                    if (presentation is "hidden") { window.Show(); }
                    await viewer.RefreshWorkAsync();
                }

                var delivered = viewer.LocalEvents.Should().ContainSingle().Which;
                delivered.Event.Source.Should().Be(LocalEventSource.ConversationTask);
                delivered.Event.TaskId.Should().Be(request.TaskId);
                delivered.Reason.Should().Be(LocalEventReason.Eligible);
                var confirmed = store.Load()!;
                confirmed.Receipts.Should().ContainSingle().Which.Event.Should().Be(delivered.Event);
                confirmed.Budgets.Should().ContainSingle().Which.Count.Should().Be(1);
                var expiry = delivered.Event.ExpiresAt;

                await viewer.RefreshWorkAsync();

                viewer.LocalEvents.Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.PresentedNoReplay);
                viewer.LocalEvents.Single().Event.ExpiresAt.Should().Be(expiry);
                store.Load()!.Budgets.Should().BeEquivalentTo(confirmed.Budgets);
                (await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(beforeRetention);
                f.Storage.Count("host_tasks").Should().Be(beforeTasks);
                f.Storage.Count("conversation_messages").Should().Be(beforeMessages);
                f.Storage.Count("security_audit_events").Should().Be(beforeAudits);
                viewer.SelectedSessionRecord.Should().Be(originalSession);
                viewer.ComposerBody.Should().Be("independent unsent input");
                window.FindControl<ListBox>("EventRecords")!.ItemsSource.Should().NotBeNull();
            }
            finally { window.Close(); }
        });
    }
}
