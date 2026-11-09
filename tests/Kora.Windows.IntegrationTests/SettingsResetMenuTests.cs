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
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Controls;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
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
        { "RefreshPlaybackVolumeCommand", "Kora playback volume", "CanChangePlaybackVolumeNative", "Refresh volume" },
        { "RefreshWindowsSpeechRateCommand", "Windows voice speed", "CanInspectWindowsSpeechRateNative", "Refresh voice speed" },
        { "RefreshResponseModeCommand", "Device-default response mode", "CanChangeResponseMode", "Refresh response mode" },
        { "RefreshConversationExecutionCommand", "Ordinary conversation execution maximum", "CanChangeConversationExecution", "Refresh ordinary conversation execution maximum" },
        { "RefreshSpeechTextCommand", "Speech captions", "CanChangeSpeechText", "Refresh captions" },
        { "RefreshSessionRetentionCommand", "Session inactivity retention", "CanChangeSessionRetentionNative", "Refresh archive and deletion settings" },
        { "RefreshOrdinaryDeadlineCommand", "Ordinary task deadline", "CanChangeOrdinaryDeadlineNative", "Refresh future ordinary task deadline" },
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
        { "ResetPlaybackVolumeCommand", "Reset volume to 100%", "CanChangePlaybackVolumeNative" },
        { "ResetWindowsSpeechRateCommand", "Reset voice speed to normal (0)", "CanChangeWindowsSpeechRateNative" },
        { "ResetDetailPresentationCommand", "Review reset to Offer", "CanControlDetailPresentationNative" },
        { "ResetResponseModeCommand", "Reset to text and speech", "CanChangeResponseMode" },
        { "ResetSpeechTextCommand", "Turn captions off", "CanChangeSpeechText" },
        { "ResetSpeechCaptionOptionCommand", "Reset selected caption option", "CanChangeSpeechText" },
        { "ResetSessionRetentionCommand", "Reset archive to 1 day and deletion to 30 days", "CanChangeSessionRetentionNative" },
        { "ResetOrdinaryDeadlineCommand", "Reset to 5 minutes", "CanChangeOrdinaryDeadlineNative" },
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
    public void Every_settings_reset_uses_its_cog_or_exact_capture_timing_review_route()
    {
        var elements = LoadSettingsMarkup().SelectMany(document => document.Descendants()).ToArray();
        var bindings = elements.SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Command" or "ResetCommand"
                && attribute.Value.StartsWith("{Binding Reset", StringComparison.Ordinal))
            .ToArray();
        bindings.Should().HaveCount(40);
        bindings.Select(attribute => attribute.Value).Distinct(StringComparer.Ordinal).Should().HaveCount(40);
        bindings.Select(attribute => attribute.Value).Should().Contain(
            ["{Binding ResetOrdinaryDeadlineCommand}", "{Binding ResetLogonStartupCommand}",
                "{Binding ResetQuietHoursCommand}", "{Binding ResetDetailPresentationCommand}",
                "{Binding ResetMarkdownDisplayCommand}"]);
        elements.Count(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal))
            .Should().Be(42);
        elements.Where(element => element.Name.LocalName is nameof(SettingActionsButton))
            .Select(element => element.Attribute("SettingName")?.Value).Should().Contain(
                ["Ordinary task deadline", "Ordinary conversation execution maximum",
                    "Scheduled proactive speech quiet hours", "Detailed result offers"]);
        foreach (var binding in bindings)
        {
            var element = binding.Parent!;
            if (string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal))
            {
                binding.Name.LocalName.Should().Be("ResetCommand");
            }
            else if (string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal))
            {
                binding.Name.LocalName.Should().Be("Command");
                binding.Value.Should().Be("{Binding ResetVoiceTimingCommand}");
                element.Attribute("Content")!.Value.Should().Be("Review reset capture timing");
                element.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanReviewVoiceTiming}");
                element.Parent!.Descendants().Attributes("Command").Select(attribute => attribute.Value)
                    .Should().Contain(["{Binding ConfirmVoiceTimingCommand}", "{Binding CancelVoiceTimingCommand}"]);
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
    [InlineData("Appearance", 17, 17, 0, 0)]
    [InlineData("Speech & audio", 9, 8, 5, 0)]
    [InlineData("Responses", 6, 6, 3, 2)]
    [InlineData("Sessions", 2, 2, 2, 0)]
    [InlineData("Logging", 2, 2, 2, 0)]
    [InlineData("Calls", 2, 2, 2, 0)]
    [InlineData("Providers", 2, 3, 2, 0)]
    [InlineData("Network", 0, 0, 0, 0)]
    [InlineData("Custom content", 1, 0, 1, 0)]
    [InlineData("Readiness", 1, 0, 1, 0)]
    public async Task Every_tab_has_top_right_cogs_with_live_named_reset_commands(
        string tabName, int expectedCogs, int expectedResets, int expectedRefreshes, int expectedInspections)
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
                var inspections = 0;
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
                            else if (item.Header is "Inspect")
                            {
                                item.Command.Should().BeSameAs(cog.SettingName switch
                                {
                                    "Detailed result offers" => fixture.Main.InspectDetailPresentationCommand,
                                    "Markdown display default" => fixture.Main.InspectMarkdownDisplayCommand,
                                    _ => throw new InvalidOperationException("Unexpected settings inspection cog: " + cog.SettingName),
                                });
                                inspections++;
                            }
                            else { resets++; }
                        }
                    }
                    finally { flyout.Hide(); }
                }
                resets.Should().Be(expectedResets);
                refreshes.Should().Be(expectedRefreshes);
                inspections.Should().Be(expectedInspections);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_cog_refreshes_the_selected_service_without_saving_and_requires_separate_field_confirmation(
        bool available)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var preferences = new LocalProviderProfilePreferences(fixture);
            var original = new ProviderProfileTuple(ProviderService.Claude, new("account"), new(1),
                new("model"), new("effort"), new("window"));
            var local = original with { Service = ProviderService.Local };
            var saved = new ProviderProfilePreferences(ProviderService.Local, [local, original]);
            preferences.Save(saved);
            var catalogue = new RefreshCatalogue(fixture.Time, available);
            var configuration = new ProviderProfileConfigurationService(preferences, catalogue, admission, fixture.Audit, fixture.Time);
            await fixture.InitializeAsync(null, providerProfileConfiguration: configuration);
            await configuration.RefreshAsync(RequestOrigin.LocalUi, () => fixture.Access.Open, fixture.Token);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            fixture.Main.SelectedProviderServiceChoice = fixture.Main.ProviderServiceChoices.Single(choice =>
                choice.Service == ProviderService.Claude);
            fixture.Main.SelectedProviderProfileModel = fixture.Main.ProviderProfileModels.Single();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                var tabs = window.FindControl<TabControl>("SettingsTabs")!;
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => tab.Header is "Providers");
                window.UpdateLayout();
                var cog = window.GetVisualDescendants().OfType<SettingActionsButton>().Single(button =>
                    button.SettingName is "Default service and model");
                cog.BringIntoView();
                window.UpdateLayout();
                using (window.CaptureRenderedFrame()) { }
                var point = cog.TranslatePoint(new Point(16, 16), window)
                    ?? throw new InvalidOperationException("The provider cog is not attached to its window.");
                var hit = window.InputHitTest(point) as Visual
                    ?? throw new InvalidOperationException("The provider cog has no rendered pointer target.");
                hit.GetSelfAndVisualAncestors().Should().Contain(cog);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout();
                var flyout = (MenuFlyout)cog.Flyout!;
                flyout.IsOpen.Should().BeTrue();
                var items = flyout.Items.OfType<MenuItem>().ToArray();
                items.Select(item => item.Header).Should().Equal(
                    "Refresh", "Reset default service", "Clear model settings for this service");
                var refresh = items[0];
                refresh.Command.Should().BeSameAs(fixture.Main.RefreshProviderProfileCommand);
                ControlAutomationPeer.CreatePeerForElement(refresh)!.GetName().Should().Be("Refresh services and models");
                preferences.Load()!.Serialize().Should().Be(saved.Serialize());
                var operation = ((AsyncCommand)refresh.Command!).ExecuteAsync();
                await catalogue.Started.Task;
                catalogue.RefreshedService.Should().Be(ProviderService.Claude);
                fixture.Main.CanChangeProviderProfile.Should().BeFalse();
                cog.IsEffectivelyEnabled.Should().BeFalse();
                refresh.IsEffectivelyEnabled.Should().BeFalse();
                await ((AsyncCommand)refresh.Command!).ExecuteAsync();
                catalogue.RefreshCount.Should().Be(1);
                catalogue.Release.SetResult();
                await operation;
                window.UpdateLayout();
                cog.IsEffectivelyEnabled.Should().BeTrue();
                refresh.IsEffectivelyEnabled.Should().BeTrue();
                preferences.Load()!.Serialize().Should().Be(saved.Serialize());
                fixture.Main.SelectedProviderProfileModel.Should().BeNull();
                fixture.Main.CanSaveProviderProfile.Should().BeFalse();
                fixture.Main.CanConfirmProviderField.Should().BeFalse();
                fixture.Main.ResponseBody.Should().Contain("Observed; no execution authority").And.Contain("\"refreshedService\":\"Claude\"");
                flyout.Hide();
                fixture.Main.SelectedProviderServiceChoice = fixture.Main.ProviderServiceChoices.Single(choice =>
                    choice.Service == ProviderService.Claude);
                if (!available)
                {
                    fixture.Main.ProviderProfileModels.Should().BeEmpty();
                    fixture.Main.ProviderSelectedServiceStatus.Should().Contain("No current models are available");
                    fixture.Main.CanReviewProviderModel.Should().BeFalse();
                    return;
                }

                fixture.Main.ProviderProfileModels.Select(model => model.Id.Value).Should().Equal("model", "model-next");
                fixture.Main.SelectedProviderProfileModel = fixture.Main.ProviderProfileModels.Single(model => model.Id.Value is "model-next");
                var review = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    ReferenceEquals(button.Command, fixture.Main.ReviewProviderModelCommand));
                await ((AsyncCommand)review.Command!).ExecuteAsync();
                fixture.Main.ProviderFieldPreviewStatus.Should().Contain("Nothing is saved until you confirm").And.Contain("model-next");
                preferences.Load()!.Serialize().Should().Be(saved.Serialize());
                var confirm = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    ReferenceEquals(button.Command, fixture.Main.ConfirmProviderFieldCommand));
                confirm.IsEffectivelyEnabled.Should().BeTrue();
                await ((AsyncCommand)confirm.Command!).ExecuteAsync();
                preferences.Load()!.Service.Should().Be(ProviderService.Local);
                preferences.Load()!.For(ProviderService.Local).Should().Be(local);
                preferences.Load()!.For(ProviderService.Claude).Should().Be(original with { Model = new("model-next") });
                fixture.Main.ResponseBody.Should().Contain("Desired configuration saved only");
                fixture.Main.CanConfirmProviderField.Should().BeFalse();
            }
            finally
            {
                catalogue.Release.TrySetResult();
                window.Close();
            }
        });
    }

    private sealed class RefreshCatalogue(TimeProvider time, bool available) : IProviderCapabilityCatalogue, IProviderCapabilityRefresh
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProviderService? RefreshedService { get; private set; }
        public int RefreshCount { get; private set; }

        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Observe(refreshed: RefreshCount > 0 && Release.Task.IsCompleted));
        }

        public async Task<ProviderCatalogueObservation> RefreshAsync(ProviderService service, Func<bool> admitted, CancellationToken token)
        {
            RefreshCount++;
            RefreshedService = service;
            admitted().Should().BeTrue();
            Started.SetResult();
            await Release.Task.WaitAsync(token);
            admitted().Should().BeTrue();
            return Observe(refreshed: true);
        }

        private ProviderCatalogueObservation Observe(bool refreshed) =>
            new(LocalProviderInstallation.Installed, Enum.GetValues<ProviderService>()
                .Where(service => service != ProviderService.Unset).Select(service =>
                {
                    var current = service is ProviderService.Local
                        || service is ProviderService.Claude && (!refreshed || available);
                    string[] ids = refreshed && service is ProviderService.Claude ? ["model", "model-next"] : ["model"];
                    return new ProviderCapabilityCatalogue(service,
                        current ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        current ? new("account") : null, new(1), time.GetUtcNow().AddMinutes(-1), time.GetUtcNow().AddMinutes(10),
                        !current ? [] : ids.Select((id, index) => new ProviderCatalogueModel(new(id), "Inert fake model",
                            new(service is ProviderService.Local ? ModelProviderIdentity.Ollama : ModelProviderIdentity.Claude,
                                new(new Guid(index + 1, 0, 0, new byte[8])), new(1)),
                            [new(new("effort"), "Exact effort", false)],
                            [new(new("window"), "Exact window", false, 4096)])).ToArray());
                }).ToArray());
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
