using System.Xml.Linq;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsResetMenuTests
{
    public static TheoryData<string, string, string?, string> MigratedRefreshes => new()
    {
        { "RefreshCommand", "Speech and audio", null, "Refresh devices and readiness" },
        { "RefreshMicrophonesCommand", "Microphone", null, "Refresh microphones" },
        { "RefreshOutputDevicesCommand", "Audio output", "CanChangeAudioOutputDevice", "Refresh speakers" },
        { "RefreshPlaybackVolumeCommand", "Kora playback volume", "CanChangePlaybackVolume", "Refresh volume" },
        { "RefreshWindowsSpeechRateCommand", "Windows voice speed", "CanInspectWindowsSpeechRateNative", "Refresh voice speed" },
        { "RefreshResponseModeCommand", "Device-default response mode", "CanChangeResponseMode", "Refresh response mode" },
        { "RefreshSpeechTextCommand", "Speech captions", "CanChangeSpeechText", "Refresh captions" },
        { "RefreshSessionRetentionCommand", "Session inactivity retention", "CanChangeSessionRetentionNative", "Refresh archive and deletion settings" },
        { "RefreshDiagnosticRetentionCommand", "Diagnostic record storage", "CanChangeDiagnosticRetentionNative", "Refresh diagnostic settings" },
        { "RefreshAuditRetentionCommand", "Security record storage", "CanChangeAuditRetentionNative", "Refresh security record settings" },
        { "GetManualCallStatusCommand", "Manual call mode", null, "Show last known call status" },
        { "RefreshInCallFeedbackCommand", "Responses during calls", "CanInspectInCallFeedbackNative", "Inspect in-call feedback preference only" },
        { "RefreshArtifactDirectoriesCommand", "Custom content sources", "CanChangeArtifactDirectories", "Refresh custom content sources" },
        { "RefreshProviderModeCommand", "AI service preferences", "CanChangeProviderMode", "Refresh service mode" },
        { "RefreshProviderProfileCommand", "Default service and model", "CanChangeProviderProfile", "Refresh services and models" },
        { "RefreshCommand", "Readiness", null, "Refresh readiness" },
    };

    public static TheoryData<string, string, string?> MigratedResets => new()
    {
        { "ResetAssistantNameCommand", "Reset name to Kora", null },
        { "ResetSpeechVoiceCommand", "Reset speech voice", null },
        { "ResetSummarySentencesCommand", "Reset sentence limit", null },
        { "ResetSummaryWordsCommand", "Reset word limit", null },
        { "ResetOutputDeviceCommand", "Use system default speaker", "CanChangeAudioOutputDevice" },
        { "ResetPlaybackVolumeCommand", "Reset volume to 100%", "CanChangePlaybackVolume" },
        { "ResetWindowsSpeechRateCommand", "Reset voice speed to normal (0)", "CanChangeWindowsSpeechRateNative" },
        { "ResetResponseModeCommand", "Reset to text and speech", "CanChangeResponseMode" },
        { "ResetSpeechTextCommand", "Turn captions off", "CanChangeSpeechText" },
        { "ResetSpeechCaptionOptionCommand", "Reset selected caption option", "CanChangeSpeechText" },
        { "ResetSessionRetentionCommand", "Reset archive to 1 day and deletion to 30 days", "CanChangeSessionRetentionNative" },
        { "ResetDiagnosticRetentionCommand", "Reset diagnostic storage to 30 days", "CanChangeDiagnosticRetentionNative" },
        { "ResetAuditRetentionCommand", "Reset security record storage to 90 days", "CanChangeAuditRetentionNative" },
        { "ResetManualCallCommand", "Reset call mode", null },
        { "ResetInCallFeedbackCommand", "Reset to on-screen responses", "CanChangeInCallFeedbackNative" },
        { "ResetProviderModeCommand", "Reset to on-device only", "CanChangeProviderMode" },
        { "ResetProviderServiceCommand", "Reset default service", "CanChangeProviderProfile" },
        { "ResetProviderProfileCommand", "Clear model settings for this service", "CanChangeProviderProfile" },
    };

    [Theory]
    [MemberData(nameof(MigratedResets))]
    public void Migrated_reset_retains_its_exact_command_label_and_guard(
        string command, string label, string? guard)
    {
        var document = LoadSettings();
        var item = document.Descendants().Single(element =>
            string.Equals(element.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal));
        item.Name.LocalName.Should().Be("MenuItem");
        item.Attribute("Header")!.Value.Should().Be(label);
        item.Attribute("IsEnabled")?.Value.Should().Be(guard is null ? null : "{Binding " + guard + "}");
        var cog = item.Ancestors().Single(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal));
        cog.Attribute("SettingName")!.Value.Should().NotBeNullOrWhiteSpace();
        var menuGuard = command switch
        {
            "ResetWindowsSpeechRateCommand" => "CanInspectWindowsSpeechRateNative",
            "ResetInCallFeedbackCommand" => "CanInspectInCallFeedbackNative",
            _ => guard,
        };
        cog.Attribute("IsEnabled")?.Value.Should().Be(menuGuard is null ? null : "{Binding " + menuGuard + "}");
        var grid = cog.Parent!;
        grid.Name.LocalName.Should().Be("Grid");
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');
        cog.Attribute("Grid.Column")!.Value.Should().Be(
            (columns.Length - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        columns[^1].Should().Be("Auto");

        if (string.Equals(command, "ResetManualCallCommand", StringComparison.Ordinal))
        {
            item.Attribute("AutomationProperties.Name")!.Value.Should().Be("Reset manual call mode to off");
        }
        else if (string.Equals(command, "ResetInCallFeedbackCommand", StringComparison.Ordinal))
        {
            item.Attribute("AutomationProperties.Name")!.Value.Should().Be("Remove saved in-call feedback override only");
        }
    }

    [Theory]
    [MemberData(nameof(MigratedRefreshes))]
    public void Refresh_is_first_in_its_cog_with_the_original_command_guard_and_accessible_name(
        string command, string settingName, string? guard, string accessibleName)
    {
        var cog = LoadSettings().Descendants().Single(element =>
            string.Equals(element.Attribute("SettingName")?.Value, settingName, StringComparison.Ordinal));
        cog.Name.LocalName.Should().Be(nameof(SettingActionsButton));
        cog.Attribute("IsEnabled")?.Value.Should().Be(guard is null ? null : "{Binding " + guard + "}");
        var item = cog.Descendants().Single(element =>
            string.Equals(element.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal));
        item.Name.LocalName.Should().Be("MenuItem");
        item.Attribute("Header")!.Value.Should().Be("Refresh");
        item.Attribute("AutomationProperties.Name")!.Value.Should().Be(accessibleName);
        item.Attribute("IsEnabled")?.Value.Should().Be(guard is null ? null : "{Binding " + guard + "}");
        var menu = item.Parent!;
        menu.Elements().First().Should().BeSameAs(item);
        if (menu.Elements().Count(element => element.Name.LocalName is "MenuItem") > 1)
        {
            item.ElementsAfterSelf().First().Name.LocalName.Should().Be("Separator");
        }
        var grid = cog.Parent!;
        grid.Name.LocalName.Should().Be("Grid");
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');
        cog.Attribute("Grid.Column")!.Value.Should().Be(
            (columns.Length - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        columns[^1].Should().Be("Auto");
    }

    [Fact]
    public void No_refresh_action_remains_as_a_button_on_the_settings_form()
    {
        var buttons = LoadSettingsMarkup().SelectMany(document => document.Descendants())
            .Where(element => element.Name.LocalName is "Button");
        buttons.Where(element =>
            (element.Attribute("Command")?.Value.StartsWith("{Binding Refresh", StringComparison.Ordinal) ?? false)
            || element.Attribute("Command")?.Value is "{Binding GetManualCallStatusCommand}"
            || (element.Attribute("Content")?.Value.StartsWith("Refresh", StringComparison.Ordinal) ?? false))
            .Should().BeEmpty();
        LoadSettings().Descendants().Count(element => element.Name.LocalName is "MenuItem"
            && element.Attribute("Header")?.Value is "Refresh").Should().Be(MigratedRefreshes.Count);
    }

    [Fact]
    public void Every_settings_reset_is_in_a_cog_menu_with_no_standalone_reset_button()
    {
        var elements = LoadSettingsMarkup().SelectMany(document => document.Descendants()).ToArray();
        var bindings = elements.SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Command" or "ResetCommand"
                && attribute.Value.StartsWith("{Binding Reset", StringComparison.Ordinal))
            .ToArray();
        bindings.Should().HaveCount(34);
        bindings.Select(attribute => attribute.Value).Distinct(StringComparer.Ordinal).Should().HaveCount(34);
        elements.Count(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal))
            .Should().Be(36);
        foreach (var binding in bindings)
        {
            var element = binding.Parent!;
            if (string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal))
            {
                binding.Name.LocalName.Should().Be("ResetCommand");
            }
            else
            {
                element.Name.LocalName.Should().Be("MenuItem");
                element.Ancestors().Should().Contain(ancestor =>
                    string.Equals(ancestor.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal));
            }
        }
        var nativeResets = elements.Where(element =>
            element.Attribute("Click")?.Value.Contains("Reset", StringComparison.Ordinal) == true).ToArray();
        nativeResets.Should().ContainSingle();
        var reset = nativeResets[0];
        reset.Name.LocalName.Should().Be("MenuItem");
        reset.Attribute("Header")!.Value.Should().Be("Use main screen");
        reset.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanSelect}");
        reset.Attribute("AutomationProperties.HelpText")!.Value.Should().Be(
            "Return captions to the main screen. Previous captions are not restored. Other caption settings are unchanged.");
        var owner = reset.Ancestors().Single(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal));
        owner.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanSelect}");
        owner.Attribute("Grid.Column")!.Value.Should().Be("1");
        elements.Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
            && (IsResetLabel(element.Attribute("Content")?.Value)
                || IsResetLabel(element.Attribute("AutomationProperties.Name")?.Value)))
            .Should().BeEmpty();
    }

    [Fact]
    public void Manual_startup_reset_retains_its_native_review_command_and_guard_in_a_named_cog()
    {
        var cog = LoadSettings().Descendants().Single(element =>
            element.Attribute("SettingName")?.Value is "Normal manual startup presentation");
        cog.Name.LocalName.Should().Be(nameof(SettingActionsButton));
        cog.Attribute("ResetCommand")!.Value.Should().Be("{Binding ResetStartupPresentationCommand}");
        cog.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanControlStartupPresentation}");
        cog.Attribute("Grid.Column")!.Value.Should().Be("1");
        var grid = cog.Parent!;
        grid.Name.LocalName.Should().Be("Grid");
        grid.Attribute("ColumnDefinitions")!.Value.Should().Be("*,Auto");
    }

    [Theory]
    [InlineData("Appearance", 16, 16, 0)]
    [InlineData("Speech & audio", 8, 7, 5)]
    [InlineData("Responses", 3, 4, 2)]
    [InlineData("Sessions", 1, 1, 1)]
    [InlineData("Logging", 2, 2, 2)]
    [InlineData("Calls", 2, 2, 2)]
    [InlineData("Providers", 2, 3, 2)]
    [InlineData("Network", 0, 0, 0)]
    [InlineData("Custom content", 1, 0, 1)]
    [InlineData("Readiness", 1, 0, 1)]
    public async Task Every_tab_has_top_right_cogs_with_live_named_reset_commands(
        string tabName, int expectedCogs, int expectedResets, int expectedRefreshes)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                var tabs = window.FindControl<TabControl>("SettingsTabs")!;
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab =>
                    string.Equals(tab.Header as string, tabName, StringComparison.Ordinal));
                window.UpdateLayout();
                window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button is not SettingActionsButton)
                    .Should().NotContain(button =>
                        IsResetLabel(button.Content as string)
                        || IsResetLabel(AutomationProperties.GetName(button)));
                var cogs = window.GetVisualDescendants().OfType<SettingActionsButton>().ToArray();
                cogs.Should().HaveCount(expectedCogs);
                var resets = 0;
                var refreshes = 0;
                foreach (var cog in cogs)
                {
                    cog.Content.Should().BeOfType<Avalonia.Controls.Shapes.Path>();
                    cog.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
                    cog.VerticalAlignment.Should().Be(VerticalAlignment.Top);
                    cog.Bounds.Top.Should().BeApproximately(0, 0.1);
                    cog.Bounds.Right.Should().BeApproximately(((Grid)cog.Parent!).Bounds.Width, 0.1);
                    ControlAutomationPeer.CreatePeerForElement(cog)!.GetName()
                        .Should().Be($"{cog.SettingName} actions");
                    var flyout = (MenuFlyout)cog.Flyout!;
                    try
                    {
                        flyout.ShowAt(cog);
                        window.UpdateLayout();
                        foreach (var item in flyout.Items.OfType<MenuItem>())
                        {
                            item.DataContext.Should().BeSameAs(cog.DataContext);
                            ControlAutomationPeer.CreatePeerForElement(item)!.GetName().Should().NotBeNullOrWhiteSpace();
                            if (cog.FindAncestorOfType<CaptionDisplaySelector>() is not null)
                            {
                                item.Command.Should().BeNull();
                                item.Header.Should().Be("Use main screen");
                                item.IsEffectivelyEnabled.Should().Be(cog.IsEffectivelyEnabled);
                            }
                            else
                            {
                                item.Command.Should().NotBeNull();
                                var admitted = cog.IsEffectivelyEnabled && item.Command!.CanExecute(null);
                                if (ReferenceEquals(item.Command, fixture.Main.ResetWindowsSpeechRateCommand))
                                {
                                    admitted &= fixture.Main.CanChangeWindowsSpeechRateNative;
                                }
                                else if (ReferenceEquals(item.Command, fixture.Main.ResetInCallFeedbackCommand))
                                {
                                    admitted &= fixture.Main.CanChangeInCallFeedbackNative;
                                }
                                item.IsEffectivelyEnabled.Should().Be(admitted);
                            }
                            if (item.Header is "Refresh") { refreshes++; }
                            else { resets++; }
                        }
                    }
                    finally { flyout.Hide(); }
                }
                resets.Should().Be(expectedResets);
                refreshes.Should().Be(expectedRefreshes);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_uses_shared_permission_grants_navigation_without_an_inline_uri_editor(bool destinationAvailable)
    {
        var network = LoadSettings().Descendants().Single(element =>
            element.Name.LocalName is "TabItem" && element.Attribute("Header")?.Value is "Network");
        network.Descendants().Where(element =>
            element.Name.LocalName is "TextBox" or "ListBox" or "ScrollableListBox").Should().BeEmpty();
        network.Descendants().SelectMany(element => element.Attributes()).Should().NotContain(attribute =>
            attribute.Value.Contains("PreapprovedUri", StringComparison.Ordinal));

        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var opens = 0;
            using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance)
            {
                InspectExactGrants = destinationAvailable ? () =>
                {
                    HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.LocalUi);
                    opens++;
                } : null,
            };
            SettingsWindow? window = null;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<SettingsWindow>((view, _) => window = view);
            fixture.Main.ShowSettings();
            opens.Should().Be(0);
            var settings = window ?? throw new InvalidOperationException("The Settings window did not open.");
            var tabs = settings.FindControl<TabControl>("SettingsTabs")!;
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => tab.Header is "Network");
            settings.UpdateLayout();
            var button = settings.FindControl<Button>("InspectNetworkPermissionGrants")!;
            button.IsEffectivelyVisible.Should().BeTrue();
            button.Content.Should().Be("Permission grants...");
            ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Be(
                "Open shared permission grants to inspect and manage URI rules and exact operation grants");
            button.IsEnabled.Should().Be(destinationAvailable);
            if (!destinationAvailable)
            {
                button.Command.Should().BeNull();
                return;
            }

            await ((AsyncCommand)button.Command!).ExecuteAsync();
            opens.Should().Be(1);
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            settings.Hide();
            Func<Task> hiddenNavigation = () => ((AsyncCommand)button.Command!).ExecuteAsync();
            await hiddenNavigation.Should().ThrowAsync<InvalidOperationException>();
            opens.Should().Be(1);
        });
    }

    [Fact]
    public async Task Clicking_speech_voice_cog_opens_its_reset_without_changing_the_voice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                window.ShowVoiceRecovery();
                window.Activate();
                window.UpdateLayout();
                var cog = window.GetVisualDescendants().OfType<SettingActionsButton>().Single(button =>
                    string.Equals(button.SettingName, "Speech voice", StringComparison.Ordinal));
                cog.BringIntoView();
                window.UpdateLayout();
                var voice = fixture.Main.SelectedVoice;
                var status = fixture.Main.SpeechSettingStatus;
                var point = cog.TranslatePoint(new Point(16, 16), window)
                    ?? throw new InvalidOperationException("The speech voice cog is not attached to its window.");

                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout();

                var flyout = (MenuFlyout)cog.Flyout!;
                flyout.IsOpen.Should().BeTrue();
                var item = flyout.Items.OfType<MenuItem>().Single();
                item.Header.Should().Be("Reset speech voice");
                item.Command.Should().BeSameAs(fixture.Main.ResetSpeechVoiceCommand);
                fixture.Main.SelectedVoice.Should().BeSameAs(voice);
                fixture.Main.SpeechSettingStatus.Should().Be(status);
                flyout.Hide();
            }
            finally { window.Close(); }
        });
    }

    private static XDocument LoadSettings() =>
        XDocument.Load(Path.Combine(FindSettingsDirectory(), "SettingsWindow.axaml"));

    private static string FindSettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kora.slnx")))
        {
            root = root.Parent;
        }
        return Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable."),
            "src", "Kora");
    }

    private static IReadOnlyList<XDocument> LoadSettingsMarkup()
    {
        var directory = FindSettingsDirectory();
        var documents = new List<XDocument> { LoadSettings() };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < documents.Count; index++)
        {
            foreach (var element in documents[index].Descendants())
            {
                var ns = element.Name.NamespaceName;
                if (element.Name.LocalName.Contains('.', StringComparison.Ordinal)
                    || !(string.Equals(ns, "using:Kora", StringComparison.Ordinal)
                        || ns.StartsWith("using:Kora.", StringComparison.Ordinal)))
                {
                    continue;
                }
                var folder = ns["using:Kora".Length..].TrimStart('.')
                    .Replace('.', Path.DirectorySeparatorChar);
                var path = Path.Combine(directory, folder, element.Name.LocalName + ".axaml");
                if (File.Exists(path) && visited.Add(path))
                {
                    documents.Add(XDocument.Load(path));
                }
            }
        }
        return documents;
    }

    private static bool IsResetLabel(string? text) =>
        text is not null && (text.StartsWith("Reset ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Reset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Use main screen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Use system default speaker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Turn captions off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Clear model settings for this service", StringComparison.OrdinalIgnoreCase));
}
