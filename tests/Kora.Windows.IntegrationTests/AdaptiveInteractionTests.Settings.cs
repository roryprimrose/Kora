using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public void Settings_view_cannot_be_created_without_an_explicit_owner() =>
        FluentActions.Invoking(() => new SettingsView()).Should().Throw<InvalidOperationException>();

    [Fact]
    public async Task Hosted_settings_uses_the_same_shell_and_never_retargets_the_conversation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            var window = f.Window!;
            var selected = f.Model.SelectedSessionRecord;
            f.Model.ComposerBody = "keep while inspecting settings";
            var captured = f.Sessions.CaptureSessionSelection(3, static () => true);
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            settings.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Settings, view));
            var standaloneOpens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SettingsWindow>((_, _) => standaloneOpens++);
            window.FindControl<Button>("SettingsActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ActiveActivity.Should().Be(WorkbenchActivity.Settings);
            var view = window.FindControl<ContentControl>("HostedViewContent")!.Content.Should().BeOfType<SettingsView>().Which;
            view.IsNativePresented.Should().BeTrue();
            window.IsSessionWorkspacePresented.Should().BeFalse();
            standaloneOpens.Should().Be(0);
            f.Model.SelectedSessionRecord.Should().Be(selected);
            f.Model.ComposerBody.Should().Be("keep while inspecting settings");
            var native = view.CapturePresentation();
            window.FindControl<Button>("SessionsActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Sessions.InteractionOperation;
            Dispatcher.UIThread.RunJobs();
            view.IsNativePresented.Should().BeFalse();
            native().Should().BeFalse();
            await captured();
            f.Model.SelectedSessionRecord.Should().Be(selected);
            f.Model.ComposerBody.Should().Be("keep while inspecting settings");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Hosted_settings_minimize_and_return_require_fresh_native_presentation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            settings.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Settings, view));
            f.Native.Main.ShowSettings();
            var view = (SettingsView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var captured = view.CapturePresentation();
            captured().Should().BeTrue();
            window.WindowState = WindowState.Minimized;
            captured().Should().BeFalse();
            view.IsNativePresented.Should().BeFalse();
            f.Native.Main.ShowApplication();
            view.IsNativePresented.Should().BeTrue();
            captured().Should().BeFalse("returning must not revive a previously captured settings review");
            view.CapturePresentation()().Should().BeTrue();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Hosted_settings_navigation_revocation_hides_the_failed_presentation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            using var settings = new SettingsWindowController(f.Native.Main, NullLogger<SettingsWindowController>.Instance);
            var admitted = true;
            settings.BindWorkbench(() => window, view =>
            {
                window.ShowHostedView(WorkbenchActivity.Settings, view);
                admitted = false;
            });
            Action navigate = () => settings.Navigate(Kora.Application.Presentation.SettingsSection.Calls, () => admitted);
            navigate.Should().Throw<InvalidOperationException>();
            window.IsVisible.Should().BeFalse();
            ((SettingsView)window.FindControl<ContentControl>("HostedViewContent")!.Content!).IsNativePresented.Should().BeFalse();
            f.Admission.Calls.Should().Be(0);
        });
    }
}
