using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Maintenance;

public sealed class MaintenanceMcpInstallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpNativeApprovalStartsOnlyTheExactInstallerAndDoesNotCloseKoraItself(bool approve)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var client = new Client(native.Time);
            var downloader = new Downloader();
            var launcher = new Launcher();
            using var state = new MaintenanceViewModel(client, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64,
                new Opener(), native.Time, new AvaloniaUiDispatcher(), new Audit(),
                NullLogger<MaintenanceViewModel>.Instance, () => 0);
            await using var commands = new MaintenanceCommands(state, native.Interactions, native.Interactions, new(native.Tasks));
            state.BindCachedCommands(commands);
            state.BindUpgradeServices(downloader, launcher);
            using var controller = new MaintenanceWindowController(native.Main, state, () => true);
            state.NetworkEnabled = true;
            var opened = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (string.Equals(window.Title, "Kora exact installer approval", StringComparison.Ordinal)) { opened.TrySetResult(window); }
            });
            var closed = 0;
            native.Main.WindowActionRequested += (_, action) => { if (action == Kora.Application.ViewModels.WindowAction.Close) { closed++; } };
            var invocation = Task.Run(async () =>
            {
                using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                    HostActivityLayer.Application, HostOperation.Request);
                return await commands.InvokeAsync(MaintenanceTools.Upgrade, """{"version":"1.2.3"}""", () => true,
                    TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken);
            var review = await opened.Task.WaitAsync(TestContext.Current.CancellationToken);
            var checkbox = review.GetLogicalDescendants().OfType<CheckBox>().Single();
            var start = review.GetLogicalDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Download and run reviewed installer", StringComparison.Ordinal));
            checkbox.IsChecked.Should().BeFalse();
            start.IsEnabled.Should().BeFalse();
            downloader.Calls.Should().Be(0);
            if (approve)
            {
                checkbox.IsChecked = true;
                start.IsEnabled.Should().BeTrue();
                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else { review.Close(); }
            var result = await invocation;

            result.Outcome.Should().Be(approve ? CapabilityOutcome.Succeeded : CapabilityOutcome.Denied);
            launcher.Assets.Should().HaveCount(approve ? 1 : 0);
            if (approve)
            {
                launcher.Assets.Single().Name.Should().Be("Kora-Setup-1.2.3-win-x64.exe");
                result.Maintenance!.Installer.Should().Be(InstallerStartOutcome.Started);
                downloader.Lease.Disposed.Should().BeTrue();
            }
            closed.Should().Be(0, "the installer owns the authenticated shutdown handshake");
        });
    }

    private sealed class Client(TimeProvider time) : IReleaseVersionMetadataClient
    {
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture,
            CancellationToken token) => CheckVersionAsync(channel, currentVersion, architecture, ReleaseVersion.Parse("1.2.3"), token);
        public Task<ReleaseCheck> CheckVersionAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture,
            ReleaseVersion version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var now = time.GetUtcNow();
            return Task.FromResult(new ReleaseCheck(ReleaseAvailability.Available, "Verified canonical test release.", now, now,
                new ReleaseMetadata(1, version, new string('a', 40), architecture,
                [
                    new(10, "Kora-1.2.3-win-x64.zip", 123, new string('b', 64)),
                    new(11, "Kora-Setup-1.2.3-win-x64.exe", 123, new string('b', 64)),
                ]) { InstallerProtocol = 2 }));
        }
    }

    private sealed class Downloader : IReleaseInstallerDownloader
    {
        public int Calls { get; private set; }
        public Lease Lease { get; } = new();
        public Task<IReleaseInstallerDownload> DownloadAsync(ReleaseMetadata release, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult<IReleaseInstallerDownload>(Lease); }
    }
    private sealed class Lease : IReleaseInstallerDownload
    {
        public bool Disposed { get; private set; }
        public string Path => @"Q:\verified-fixture\Kora-Setup-1.2.3-win-x64.exe";
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Launcher : IReleaseInstallerLauncher
    {
        public List<ReleaseAsset> Assets { get; } = [];
        public Task LaunchAsync(string path, ReleaseAsset installer, Func<bool> admitted, CancellationToken token)
        { token.ThrowIfCancellationRequested(); admitted().Should().BeTrue(); Assets.Add(installer); return Task.CompletedTask; }
    }
    private sealed class Opener : ICanonicalReleasePageOpener
    {
        public Task OpenAsync(ReleaseVersion version, CancellationToken token) => throw new InvalidOperationException("No browser is part of installer approval.");
    }
    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
