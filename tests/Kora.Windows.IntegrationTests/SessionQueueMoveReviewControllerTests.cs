using System.Globalization;

using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionQueueMoveReviewControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactCommandPreparesSameNativeReviewWithCompletePairDisclosureAndNoOrderOrTaskWrite(bool up)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = CreateFixture();
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opens++; });
            controller.Open();
            var viewer = (SessionsViewModel)window!.DataContext!;
            await controller.NavigateAsync(f.LifecycleTarget!.SessionId, () => true, f.Token);
            await viewer.EnqueueVersionAsync();
            viewer.ChooseNoQueuePrerequisite();
            await viewer.EnqueueCapabilitiesAsync();
            var before = await f.Interactions.ReadQueueAsync(f.LifecycleTarget.SessionId, f.Token);
            var tasks = await f.Interactions.ReadTaskPageAsync(f.LifecycleTarget.SessionId, null, 50, f.Token);
            var retention = await f.Interactions.ReadRetentionAsync(f.LifecycleTarget.SessionId, f.Token);
            var selected = before.Entries[up ? 1 : 0];
            var neighbor = before.Entries[up ? 0 : 1];
            f.Main.CommandText = Input(before.SessionId.Value, selected.Request.TaskId.Value, neighbor.Request.TaskId.Value, up);
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            viewer.CanConfirmQueueMove.Should().BeTrue();
            viewer.QueueMoveStatus.Should().Contain("Current:").And.Contain("Proposed:")
                .And.Contain("application.get_version").And.Contain("capabilities.list")
                .And.Contain("dependency None").And.Contain("control revision").And.Contain("paused False held False")
                .And.Contain(before.SessionId.Value.ToString("D")).And.Contain(before.DispatchControl!.RunId.ToString("D"));
            foreach (var entry in before.Entries)
            {
                viewer.QueueMoveStatus.Should().Contain(entry.Request.TaskId.Value.ToString("D"))
                    .And.Contain("revision " + entry.Revision.Value.ToString(CultureInfo.InvariantCulture))
                    .And.Contain(entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
            }
            f.Main.Transcript.Should().StartWith("Queue move review prepared.").And.NotContain(selected.Request.TaskId.Value.ToString("D"));
            (await f.Interactions.ReadQueueAsync(before.SessionId, f.Token)).MatchesRecordedState(before).Should().BeTrue();
            (await f.Interactions.ReadTaskPageAsync(before.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(tasks);
            (await f.Interactions.ReadRetentionAsync(before.SessionId, f.Token)).Should().Be(retention);
            await viewer.CancelQueueMoveAsync();
            viewer.CanConfirmQueueMove.Should().BeFalse();
            await f.Main.RunTypedCommand.ExecuteAsync();
            viewer.CanConfirmQueueMove.Should().BeTrue();
            // Only the deliberate existing native action changes ordering, and only once.
            await viewer.ConfirmQueueMoveAsync();
            (await f.Interactions.ReadQueueAsync(before.SessionId, f.Token)).Entries.Should().Equal(before.Entries.Reverse());
            viewer.CanConfirmQueueMove.Should().BeFalse();
            window.ClosePrivate();
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("discard")]
    [InlineData("input")]
    [InlineData("private")]
    [InlineData("close")]
    public async Task DirtyComposerUsesExistingDecisionAndNeverPublishesAfterDeferredAdmissionRetires(string mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = CreateFixture();
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            controller.Open();
            var viewer = (SessionsViewModel)window!.DataContext!;
            await controller.NavigateAsync(f.LifecycleTarget!.SessionId, () => true, f.Token);
            await viewer.EnqueueVersionAsync();
            viewer.ChooseNoQueuePrerequisite();
            await viewer.EnqueueCapabilitiesAsync();
            var before = await f.Interactions.ReadQueueAsync(f.LifecycleTarget.SessionId, f.Token);
            viewer.ComposerBody = "retain exact private draft";
            var decisions = 0;
            viewer.DecideComposerDraft = () =>
            {
                decisions++;
                if (mode is "input") { f.Main.CommandText = "changed input"; }
                if (mode is "private") { f.Access.SetOpen(false); }
                if (mode is "close") { window.ClosePrivate(); }
                return Task.FromResult(mode is "discard" ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel);
            };
            f.Main.CommandText = Input(before.SessionId.Value, before.Entries[1].Request.TaskId.Value, before.Entries[0].Request.TaskId.Value, true);
            await f.Main.RunTypedCommand.ExecuteAsync();
            decisions.Should().Be(1);
            if (mode is "discard")
            {
                viewer.CanConfirmQueueMove.Should().BeTrue();
                viewer.ComposerBody.Should().BeEmpty();
                f.Main.CommandText = "edited after publication";
                viewer.CanConfirmQueueMove.Should().BeFalse();
                await viewer.ConfirmQueueMoveAsync();
            }
            else
            {
                viewer.CanConfirmQueueMove.Should().BeFalse();
                if (mode is "cancel" or "input") { viewer.ComposerBody.Should().Be("retain exact private draft"); }
            }
            (await f.Interactions.ReadQueueAsync(before.SessionId, f.Token)).MatchesRecordedState(before).Should().BeTrue();
            window.ClosePrivate();
        });
    }

    [Theory]
    [InlineData("nonadjacent")]
    [InlineData("missing")]
    [InlineData("direction")]
    [InlineData("same")]
    public async Task WrongExactPairRefusesWholePreparationWithoutSubstitution(string mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = CreateFixture();
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            controller.Open();
            var viewer = (SessionsViewModel)window!.DataContext!;
            await controller.NavigateAsync(f.LifecycleTarget!.SessionId, () => true, f.Token);
            await viewer.EnqueueVersionAsync();
            viewer.ChooseNoQueuePrerequisite();
            await viewer.EnqueueCapabilitiesAsync();
            viewer.ChooseNoQueuePrerequisite();
            await viewer.EnqueueReadinessAsync();
            var before = await f.Interactions.ReadQueueAsync(f.LifecycleTarget.SessionId, f.Token);
            var selected = before.Entries[2].Request.TaskId.Value;
            var neighbor = mode switch
            {
                "missing" => Guid.NewGuid(),
                "same" => selected,
                "nonadjacent" => before.Entries[0].Request.TaskId.Value,
                _ => before.Entries[1].Request.TaskId.Value,
            };
            f.Main.CommandText = Input(before.SessionId.Value, selected, neighbor, mode is not "direction");
            await f.Main.RunTypedCommand.ExecuteAsync();
            viewer.CanConfirmQueueMove.Should().BeFalse();
            (await f.Interactions.ReadQueueAsync(before.SessionId, f.Token)).MatchesRecordedState(before).Should().BeTrue();
            window.ClosePrivate();
        });
    }

    private static string Input(Guid session, Guid task, Guid neighbor, bool up) =>
        "session queue move review " + session.ToString("D") + " " + task.ToString("D") + " " + neighbor.ToString("D") + (up ? " up" : " down");

    private static NativeUxFixtureSession CreateFixture()
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
        Directory.CreateDirectory(parent);
        return new(parent);
    }
}
