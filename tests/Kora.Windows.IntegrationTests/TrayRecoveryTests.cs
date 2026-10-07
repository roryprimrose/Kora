using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

public sealed class TrayRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_microphone_selection_mark_does_not_claim_capture_or_reveal_endpoint_ID(bool selected)
    {
        var item = SystemTrayController.CreateMicrophoneItem(
            new("private-endpoint-id", "Headset"), selected, "available");
        item.ToggleType.Should().Be(MenuItemToggleType.Radio);
        item.IsChecked.Should().Be(selected);
        item.IsEnabled.Should().BeTrue();
        item.Header.Should().NotContain("private-endpoint-id");
        if (selected) { item.Header.Should().Contain("selected preference, not capture"); }
    }

    [Fact]
    public void Native_System_and_unavailable_pinned_endpoint_remain_distinct_without_launching_the_app()
    {
        var system = SystemTrayController.CreateMicrophoneItem(
            SystemAudioDevices.Microphone, selected: false, "Windows default; unavailable");
        var pinned = SystemTrayController.CreateMicrophoneItem(
            new("private-endpoint", "Previous headset"), selected: true, "unavailable; preference retained", selectable: false);
        var menu = new NativeMenu();
        menu.Add(system);
        menu.Add(pinned);
        menu.Items.Should().HaveCount(2);
        system.IsEnabled.Should().BeTrue();
        system.Header.Should().Be("System (Windows default; unavailable)");
        pinned.IsEnabled.Should().BeFalse();
        pinned.IsChecked.Should().BeTrue();
        pinned.Header.Should().Contain("unavailable; preference retained").And.NotContain("private-endpoint");
    }

    [Fact]
    public void Tray_and_Settings_reach_the_same_passive_card_with_exact_native_click_binding()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory!.FullName;
        string Read(string name) => File.ReadAllText(Path.Combine(root, "src", "Kora", name));
        var app = Read("App.axaml.cs");
        app.Should().Contain("microphoneRecovery.Open").And.Contain("microphoneRecovery?.Dispose()");
        Read("SystemTrayController.cs").Should().Contain("Choose microphone (native recovery)")
            .And.Contain("RunAfterNativeMenuCloses(chooseMicrophone)");
        Read("SettingsWindow.axaml").Should().Contain("x:Name=\"ChooseMicrophone\"")
            .And.NotContain("SelectedItem=\"{Binding SelectedMicrophone}\"");
        Read("SettingsWindowController.cs").Should().Contain("new SettingsWindow(viewModel, chooseMicrophone)");
        Read("MicrophoneRecoveryWindow.axaml.cs").Should().Contain("var exactChoice = model.Draft")
            .And.Contain("model.SaveAsync(exactChoice)")
            .And.Contain("var exactSelection = model.DisplayedSelection")
            .And.Contain("model.EnableAsync(exactSelection)")
            .And.Contain("model.Dispose()")
            .And.Contain("Opened += (_, _) => Run(model.RefreshAsync)");
        var card = Read("MicrophoneRecoveryWindow.axaml");
        card.Should().Contain("Save preference only").And.Contain("PTT readiness only")
            .And.Contain("IsEnabled=\"{Binding CanEnable}\"").And.Contain("Close without changes")
            .And.NotContain("Topmost=\"True\"").And.NotContain("Test microphone");
        Read("MicrophoneRecoveryWindowController.cs").Should().Contain("main.CanUseTrayMicrophoneRecovery")
            .And.Contain("main.PrivacyClosureRequested -= OnPrivacyClosure")
            .And.NotContain("NativeQuestionHost").And.NotContain("CreateSession");
    }

    [Fact]
    public void Native_tray_routes_preserve_existing_host_navigation_and_guard_delayed_actions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Kora", "SystemTrayController.cs"));
        source.Should().Contain("menu.Opening += OnMenuOpening")
            .And.Contain("menu.Opening -= OnMenuOpening")
            .And.Contain("EnableListeningFromTrayAsync(revision)")
            .And.Contain("DisableListeningFromTrayAsync")
            .And.Contain("StopSpeakingFromTrayAsync")
            .And.Contain("if (!disposed) { action(); }")
            .And.Contain("ClickSequenceOutcome.DoubleClick")
            .And.Contain("ClickSequenceOutcome.SingleClick")
            .And.Contain("Skill packages (inspection only)")
            .And.Contain("Sessions")
            .And.Contain("Release maintenance (notify-only)")
            .And.Contain("ShowDocumentation")
            .And.Contain("viewModel.ExitAsync()");
        source.Should().NotContain("viewModel.ListeningStatus").And.NotContain("· {device.Id}");
    }
}
