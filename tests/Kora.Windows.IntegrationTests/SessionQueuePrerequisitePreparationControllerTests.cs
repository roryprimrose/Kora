using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionQueuePrerequisitePreparationControllerTests
{
    [Fact]
    public async Task HelpNeverOpensAndOnlyTheOriginalPrivateOwnerCanApplyTheExactPreparedChoice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var controller = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => { window = view; opens++; });
            f.Main.CommandText = "session queue prerequisite help";
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(0);
            var target = f.LifecycleTarget!;
            var metadata = await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token);
            var tasks = await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token);
            f.Main.CommandText = "session queue prerequisite prepare " + target.SessionId.Value.ToString("D") + " none";
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            var model = (SessionsViewModel)window!.DataContext!;
            model.SelectedSessionRecord.Should().BeNull("the command proposes, never replaces work or prerequisite selection");
            model.CanApplyPreparedQueuePrerequisite.Should().BeTrue();
            f.Main.ResponseBody.Should().NotContain(target.SessionId.Value.ToString("D"));
            var old = model.PreparedQueuePrerequisiteChoice;
            controller.OpenCompact();
            window.Close();
            model.CanApplyPreparedQueuePrerequisite.Should().BeFalse();
            await model.ApplyPreparedQueuePrerequisiteAsync(old);
            model.SelectedSessionRecord.Should().BeNull();
            await f.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(2);
            model.CanApplyPreparedQueuePrerequisite.Should().BeTrue();
            await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
            model.SelectedSessionRecord.Should().Be(metadata);
            model.SelectedQueuePrerequisite.Should().BeNull();
            (await f.Interactions.ReadTaskPageAsync(target.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(tasks);
            (await f.Interactions.ReadMetadataAsync(target.SessionId, f.Token)).Should().Be(metadata);
            var command = new SessionQueuePrerequisitePrepareCommand(target.SessionId.Value, null);
            var denied = () => controller.PrepareQueuePrerequisiteAsync(command, RequestOrigin.HostSystem, () => true, f.Token);
            await denied.Should().ThrowAsync<OperationCanceledException>();
            window!.ClosePrivate();
            controller.Dispose();
            var disposed = () => controller.PrepareQueuePrerequisiteAsync(command, RequestOrigin.LocalUi, () => true, f.Token);
            await disposed.Should().ThrowAsync<OperationCanceledException>();
        });
    }
}
