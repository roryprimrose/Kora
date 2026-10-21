using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionQueueCancellationPreparationControllerTests
{
    [Fact]
    public async Task TypedPreparationOpensExactNativeCompleteReviewAndClosingOwnerRevokesConfirmation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            var session = f.LifecycleTarget!;
            for (var i = 0; i < 2; i++)
            {
                var observed = await f.Interactions.ReadQueueAsync(session.SessionId, f.Token);
                await f.Sessions.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, session.SessionId.Value, session.Generation.Value)
                {
                    QueueRevision = observed.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(),
                }, RequestOrigin.LocalUi, () => true, f.Token);
            }
            var original = await f.Interactions.ReadQueueAsync(session.SessionId, f.Token);
            var tasks = await f.Interactions.ReadTaskPageAsync(session.SessionId, null, 50, f.Token);
            SessionsWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opens++; });
            f.Main.CommandText = "session queue cancellation review " + session.SessionId.Value.ToString("D") + " "
                + string.Join(',', original.Entries.Reverse().Select(entry => entry.Request.TaskId.Value.ToString("D")));
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            window!.IsVisible.Should().BeTrue();
            var model = (SessionsViewModel)window.DataContext!;
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(session.SessionId);
            model.CanConfirmPendingCancellation.Should().BeTrue(model.Status);
            model.PendingCancellationStatus.Should().Contain("subset: 2");
            f.Main.ResponseTitle.Should().Be("Queue cancellation review prepared.");
            f.Main.ResponseBody.Should().NotContain(session.SessionId.Value.ToString("D"));
            (await f.Interactions.ReadTaskPageAsync(session.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(tasks);
            (await f.Interactions.ReadQueueAsync(session.SessionId, f.Token)).Should().BeEquivalentTo(original);
            controller.OpenCompact();
            window.Close();
            model.CanConfirmPendingCancellation.Should().BeFalse("the original native review owner closed even if Compact survives");
            controller.Dispose();
            var unavailable = () => controller.PrepareQueueCancellationAsync(
                new(session.SessionId.Value, [original.Entries[0].Request.TaskId.Value]),
                RequestOrigin.LocalUi, () => true, f.Token);
            await unavailable.Should().ThrowAsync<OperationCanceledException>();
        });
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("control")]
    [InlineData("buffer")]
    [InlineData("selection")]
    public async Task NativeOpenCallbackCannotGrantConfirmationAfterOriginalOwnerOrControlChanged(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            var session = f.LifecycleTarget!;
            var task = Guid.NewGuid();
            await f.Sessions.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, session.SessionId.Value, session.Generation.Value)
                { WorkRequestId = Guid.NewGuid(), TaskId = task }, RequestOrigin.LocalUi, () => true, f.Token);
            SessionsViewModel? model = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) =>
            {
                model = (SessionsViewModel)view.DataContext!;
                if (change is "closed") { view.Close(); }
                else if (change is "control") { f.Access.SetOpen(false); }
                else if (change is "buffer") { model.ComposerBody = "new original input"; }
                else { model.SelectTask(null); }
            });
            f.Main.CommandText = "session queue cancellation review " + session.SessionId.Value.ToString("D") + " " + task.ToString("D");
            await f.Main.RunTypedCommand.ExecuteAsync();
            model!.CanConfirmPendingCancellation.Should().BeFalse();
            (await f.Interactions.ReadQueueAsync(session.SessionId, f.Token)).Entries.Single().Request.TaskId.Value.Should().Be(task);
        });
    }
}
