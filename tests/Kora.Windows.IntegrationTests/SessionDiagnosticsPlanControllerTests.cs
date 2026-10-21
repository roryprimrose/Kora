using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionDiagnosticsPlanControllerTests
{
    [Fact]
    public async Task TypedCommandOpensExactExistingNativeReviewOnlyAndOwnerClosureRetiresIt()
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
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            var target = f.LifecycleTarget!;
            var before = await f.Interactions.ReadQueueAsync(target.SessionId, f.Token);
            f.Main.CommandText = "session diagnostics plan " + target.SessionId.Value.ToString("D") + " version,readiness";
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.ResponseTitle.Should().Be("Session diagnostics plan prepared.");
            window.Should().NotBeNull();
            var model = (SessionsViewModel)window!.DataContext!;
            model.SelectedSessionRecord!.Authority.Should().Be(target);
            model.DiagnosticsVersion.Should().BeTrue();
            model.DiagnosticsCapabilities.Should().BeFalse();
            model.DiagnosticsReadiness.Should().BeTrue();
            model.DiagnosticsRuntimes.Should().BeFalse();
            model.CanConfirmDiagnosticsPlan.Should().BeTrue();
            (await f.Interactions.ReadQueueAsync(target.SessionId, f.Token)).Should().BeEquivalentTo(before);
            controller.OpenCompact();
            window.Close();
            model.CanConfirmDiagnosticsPlan.Should().BeFalse();
            controller.Dispose();
        });
    }
}
