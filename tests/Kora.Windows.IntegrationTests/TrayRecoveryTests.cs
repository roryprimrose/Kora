using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class TrayRecoveryTests
{
    private static readonly string[] NavigationLabels =
        ["New request...", "Sessions...", "Choose microphone...", "Evidence & diagnostics..."];

    [Fact]
    public Task TrayShowsNineRootRowsAndOnlyVoiceAndHelpSubmenus() => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        var opened = new List<string>();
        var navigations = NavigationLabels
            .ToDictionary(label => label, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                StringComparer.Ordinal);
        void Open(string label) { opened.Add(label); navigations[label].SetResult(); }
        var selection = fixture.Main.SelectedMicrophone;
        using var tray = new SystemTrayController(fixture.Main, NullLogger<SystemTrayController>.Instance,
            inspectEvidence: () => Open("Evidence & diagnostics..."), inspectSessions: () => Open("Sessions..."),
            chooseMicrophone: () => Open("Choose microphone..."), newRequest: () => Open("New request..."));
        var icon = TrayIcon.GetIcons(Avalonia.Application.Current!)!.Single();
        var menu = icon.Menu!;
        var roots = menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).ToArray();
        roots.Select(item => item.Header).Should().Equal(
            fixture.Main.TrayInputStatus, "Show Kora", "New request...", "Sessions...",
            "Disable listening", "Voice & audio", "Settings...", "Help", "Exit Kora");
        menu.Items.OfType<NativeMenuItemSeparator>().Should().HaveCount(3);
        roots[0].IsEnabled.Should().BeFalse();
        var voice = roots.Single(item => string.Equals(item.Header, "Voice & audio", StringComparison.Ordinal)).Menu!;
        voice.Items.Cast<NativeMenuItem>().Select(item => item.Header).Should().Equal(
            "Enable listening (push-to-talk readiness)", "Choose microphone...", "Speech & audio settings...");
        var help = roots.Single(item => string.Equals(item.Header, "Help", StringComparison.Ordinal)).Menu!;
        help.Items.Cast<NativeMenuItem>().Select(item => item.Header).Should().Equal(
            "Documentation", "Evidence & diagnostics...");
        opened.Should().BeEmpty();
        foreach (var label in navigations.Keys)
        {
            var item = roots.Concat(voice.Items.Cast<NativeMenuItem>()).Concat(help.Items.Cast<NativeMenuItem>())
                .Single(item => string.Equals(item.Header, label, StringComparison.Ordinal));
            var before = opened.Count;
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            opened.Should().HaveCount(before, "native callbacks must wait for menu dismissal");
            await navigations[label].Task.WaitAsync(fixture.Token);
            opened.Should().HaveCount(before + 1).And.EndWith(label);
        }
        fixture.Main.IsVoiceEnabled.Should().BeFalse();
        fixture.Main.SelectedMicrophone.Should().Be(selection);
        icon.ToolTipText.Should().Be("Kora - " + fixture.Main.TrayInputStatus);
    });

    [Fact]
    public Task OnlyTheExactShowPresenterIsBoldAndAllPrimaryClicksShowKora() => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        var application = Avalonia.Application.Current!;
        var originalStyles = application.Styles.Count;
        using var tray = new SystemTrayController(fixture.Main, NullLogger<SystemTrayController>.Instance);
        var roots = TrayIcon.GetIcons(application)!.Single().Menu!.Items.OfType<NativeMenuItem>().ToArray();
        var settings = roots.Single(item => string.Equals(item.Header, "Settings...", StringComparison.Ordinal));
        var show = roots.Single(item => string.Equals(item.Header, "Show Kora", StringComparison.Ordinal));
        var defaultPresenter = new MenuItem { Header = show.Header, DataContext = show };
        var unrelatedPresenter = new MenuItem { Header = show.Header, DataContext = settings };
        var window = new Window { Content = new StackPanel { Children = { defaultPresenter, unrelatedPresenter } } };
        try
        {
            window.Show();
            defaultPresenter.FontWeight.Should().Be(FontWeight.Bold);
            unrelatedPresenter.FontWeight.Should().NotBe(FontWeight.Bold);
            show.Header = "Show renamed assistant";
            defaultPresenter.FontWeight.Should().Be(FontWeight.Bold);
            var settingsRequests = 0;
            var showRequests = 0;
            var settingsOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Main.SettingsRequested += (_, _) => { settingsRequests++; settingsOpened.TrySetResult(); };
            fixture.Main.WindowActionRequested += (_, action) => { if (action == WindowAction.Show) { showRequests++; } };
            tray.HandlePrimaryClick();
            showRequests.Should().Be(1, "the first click reveals immediately without a double-click delay");
            settingsRequests.Should().Be(0);
            tray.HandlePrimaryClick();
            showRequests.Should().Be(2, "a second click repeats the same Show action, never Settings");
            settingsRequests.Should().Be(0);
            ((INativeMenuItemExporterEventsImplBridge)settings).RaiseClicked();
            settingsRequests.Should().Be(0, "explicit Settings navigation still waits for menu dismissal");
            await settingsOpened.Task.WaitAsync(fixture.Token);
            settingsRequests.Should().Be(1);
            showRequests.Should().Be(2);
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            tray.Dispose();
            application.Styles.Count.Should().Be(originalStyles);
            defaultPresenter.FontWeight.Should().NotBe(FontWeight.Bold);
            tray.HandlePrimaryClick();
            showRequests.Should().Be(2);
            settingsRequests.Should().Be(1);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnboundOptionalTrayDestinationsAreAbsentAndDisposalRemovesTheIcon() => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
        await fixture.InitializeAsync();
        using var tray = new SystemTrayController(fixture.Main, NullLogger<SystemTrayController>.Instance);
        var menu = TrayIcon.GetIcons(Avalonia.Application.Current!)!.Single().Menu!;
        var roots = menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).ToArray();
        roots.Select(item => item.Header).Should().Equal(fixture.Main.TrayInputStatus, "Show Kora",
            "Disable listening", "Voice & audio", "Settings...", "Help", "Exit Kora");
        roots.Single(item => string.Equals(item.Header, "Voice & audio", StringComparison.Ordinal))
            .Menu!.Items.Cast<NativeMenuItem>().Select(item => item.Header).Should().Equal(
                "Enable listening (push-to-talk readiness)", "Speech & audio settings...");
        roots.Single(item => string.Equals(item.Header, "Help", StringComparison.Ordinal))
            .Menu!.Items.Cast<NativeMenuItem>().Select(item => item.Header).Should().Equal("Documentation");
        tray.Dispose();
        TrayIcon.GetIcons(Avalonia.Application.Current!).Should().BeNull();
    });

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
        Read("SystemTrayController.cs").Should().Contain("Choose microphone...")
            .And.Contain("RunAfterNativeMenuCloses(choose)");
        Read("SettingsWindow.axaml").Should().Contain("x:Name=\"ChooseMicrophone\"")
            .And.NotContain("SelectedItem=\"{Binding SelectedMicrophone}\"");
        Read("SettingsWindowController.cs").Should().Contain("new SettingsWindow(viewModel, chooseMicrophone, captionDisplay)");
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
        var normalizedSource = source.ReplaceLineEndings("\n");
        source.Should().Contain("EnableListeningFromTrayAsync(revision)")
            .And.Contain("DisableListeningFromTrayAsync")
            .And.NotContain("StopSpeakingFromTrayAsync")
            .And.Contain("if (!disposed) { continuation(); }")
            .And.Contain("NativeMenuDismissalDelay, DispatcherPriority.Input")
            .And.NotContain("ClickSequenceOutcome")
            .And.NotContain("trayClickTimer")
            .And.NotContain("Skill packages (inspection only)")
            .And.Contain("Sessions")
            .And.NotContain("Release maintenance (notify-only)")
            .And.Contain("ShowDocumentation")
            .And.Contain("viewModel.ExitAsync()");
        normalizedSource.Should().NotContain("menu.Opening")
            .And.NotContain("OnMenuOpening");
        File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", "SessionAttachmentWindowController.cs"))
            .Should().Contain("PostForCurrentInspection(CloseInspection)")
            .And.Contain("PostForCurrentInspection(() => OnRevoked(revoked))")
            .And.Contain("PostForCurrentInspection(() => OnAttachmentRevoked(revoked), exactView: true)")
            .And.Contain("Dispatcher.UIThread.Post(() =>")
            .And.Contain("ReferenceEquals(expectedLifetime, lifetime)")
            .And.Contain("!exactView || expected == Volatile.Read(ref viewRevision)")
            .And.NotContain("Dispatcher.UIThread.Invoke(CloseInspection)")
            .And.NotContain("Dispatcher.UIThread.Invoke(() => OnRevoked(revoked))")
            .And.NotContain("Dispatcher.UIThread.Invoke(() => OnAttachmentRevoked(revoked))");
        File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", "AttachmentCatalogueWindowController.cs"))
            .Should().Contain("Dispatcher.UIThread.Post(OnInspectionRevoked)")
            .And.NotContain("Dispatcher.UIThread.Invoke(OnInspectionRevoked)");
        source.Should().NotContain("· {device.Id}");
    }
}
