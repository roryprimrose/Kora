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

using Kora.Controls;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsResetMenuTests
{
    public static TheoryData<string, string, string?> MigratedResets => new()
    {
        { "ResetAssistantNameCommand", "Reset name to Kora", null },
        { "ResetSpeechVoiceCommand", "Reset speech voice", null },
        { "ResetSummarySentencesCommand", "Reset sentence cap", null },
        { "ResetSummaryWordsCommand", "Reset word cap", null },
        { "ResetOutputDeviceCommand", "Reset output to System", "CanChangeAudioOutputDevice" },
        { "ResetPlaybackVolumeCommand", "Reset volume to 100%", "CanChangePlaybackVolume" },
        { "ResetWindowsSpeechRateCommand", "Reset Windows rate to normal (0)", "CanChangeWindowsSpeechRateNative" },
        { "ResetResponseModeCommand", "Reset device default to Hybrid", "CanChangeResponseMode" },
        { "ResetSpeechTextCommand", "Reset speech text to Off", "CanChangeSpeechText" },
        { "ResetSpeechCaptionOptionCommand", "Reset selected caption option", "CanChangeSpeechText" },
        { "ResetQueuePendingCommand", "Reset pending capacity to 10", "CanChangeQueueConfigurationNative" },
        { "ResetQueueSlotsCommand", "Reset fixed read-only slots to 1", "CanChangeQueueConfigurationNative" },
        { "ResetQueueLifetimeCommand", "Reset future pending lifetime to 30 minutes", "CanChangeQueueConfigurationNative" },
        { "ResetQueueActiveBudgetCommand", "Reset future active budget to 5 minutes", "CanChangeQueueConfigurationNative" },
        { "ResetSessionRetentionCommand", "Reset future session retention to 1 / 30 days", "CanChangeSessionRetentionNative" },
        { "ResetDiagnosticRetentionCommand", "Reset future SQLite retention to 30 days", "CanChangeDiagnosticRetentionNative" },
        { "ResetAuditRetentionCommand", "Reset future audit retention to 90 days", "CanChangeAuditRetentionNative" },
        { "ResetManualCallCommand", "Reset manual off", null },
        { "ResetInCallFeedbackCommand", "Reset call feedback to UI", "CanChangeInCallFeedbackNative" },
        { "ResetProviderModeCommand", "Reset provider mode to LocalOnly", "CanChangeProviderMode" },
        { "ResetProviderServiceCommand", "Reset app service using installed-or-Unset rule", "CanChangeProviderProfile" },
        { "ResetProviderProfileCommand", "Reset selected service tuple to unresolved; retain other services", "CanChangeProviderProfile" },
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
        cog.Attribute("IsEnabled")?.Value.Should().Be(item.Attribute("IsEnabled")?.Value);
        var grid = cog.Parent!;
        grid.Name.LocalName.Should().Be("Grid");
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');
        cog.Attribute("Grid.Column")!.Value.Should().Be(
            (columns.Length - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        columns[^1].Should().Be("Auto");

        if (string.Equals(command, "ResetManualCallCommand", StringComparison.Ordinal))
        {
            item.Attribute("AutomationProperties.Name")!.Value.Should().Be("Reset manual call layer off for this run");
        }
        else if (string.Equals(command, "ResetInCallFeedbackCommand", StringComparison.Ordinal))
        {
            item.Attribute("AutomationProperties.Name")!.Value.Should().Be("Remove saved in-call feedback override only");
        }
    }

    [Fact]
    public void Every_settings_reset_is_in_a_cog_menu_with_no_standalone_reset_button()
    {
        var elements = LoadSettingsMarkup().SelectMany(document => document.Descendants()).ToArray();
        var bindings = elements.SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Command" or "ResetCommand"
                && attribute.Value.StartsWith("{Binding Reset", StringComparison.Ordinal))
            .ToArray();
        bindings.Should().HaveCount(36);
        bindings.Select(attribute => attribute.Value).Distinct(StringComparer.Ordinal).Should().HaveCount(36);
        elements.Count(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal))
            .Should().Be(31);
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
        reset.Attribute("Header")!.Value.Should().Be("Return to primary");
        reset.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanSelect}");
        reset.Attribute("AutomationProperties.HelpText")!.Value.Should().Be(
            "Explicitly return to the current primary display. Does not restore retired text or repair caption preferences.");
        var owner = reset.Ancestors().Single(element =>
            string.Equals(element.Name.LocalName, nameof(SettingActionsButton), StringComparison.Ordinal));
        owner.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanSelect}");
        owner.Attribute("Grid.Column")!.Value.Should().Be("1");
        elements.Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
            && (IsResetLabel(element.Attribute("Content")?.Value)
                || IsResetLabel(element.Attribute("AutomationProperties.Name")?.Value)))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("Appearance", 14, 14)]
    [InlineData("Speech & audio", 6, 7)]
    [InlineData("Responses", 3, 4)]
    [InlineData("Sessions", 2, 5)]
    [InlineData("Logging", 2, 2)]
    [InlineData("Calls", 2, 2)]
    [InlineData("Providers", 2, 3)]
    public async Task Every_tab_has_top_right_cogs_with_live_named_reset_commands(
        string tabName, int expectedCogs, int expectedResets)
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
                foreach (var cog in cogs)
                {
                    cog.Content.Should().BeOfType<PathIcon>();
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
                                item.Header.Should().Be("Return to primary");
                                item.IsEffectivelyEnabled.Should().Be(cog.IsEffectivelyEnabled);
                            }
                            else
                            {
                                item.Command.Should().NotBeNull();
                                item.IsEffectivelyEnabled.Should().Be(
                                    cog.IsEffectivelyEnabled && item.Command!.CanExecute(null));
                            }
                            resets++;
                        }
                    }
                    finally { flyout.Hide(); }
                }
                resets.Should().Be(expectedResets);
            }
            finally { window.Close(); }
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
            || string.Equals(text, "Return to primary", StringComparison.OrdinalIgnoreCase));
}
