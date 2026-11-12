using Avalonia.Controls;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Controls;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsBrowserTests
{
    private static readonly string[] ExactQueries = ["test.option", "TEST.OPTION", "test.legacy-option", "Specific setting"];

    [Fact]
    public async Task ExactIdsAndTitlesExcludeIncidentalDescriptionMatchesAndIdPrefixes()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var first = new Border { Child = new TextBlock { Text = "Specific setting", Classes = { "settings-title" } } };
            var other = new Border { Child = new TextBlock { Text = "Specific setting test.option", Classes = { "settings-description" } } };
            SettingsSearch.SetId(first, "test.option");
            SettingsSearch.SetAliases(first, "test.legacy-option");
            SettingsSearch.SetId(other, "test.option-extra");
            using var browser = new SettingsBrowser
            {
                Items = { new SettingsPage { Header = "Test", Content = new StackPanel { Children = { first, other } } } },
            };
            browser.Initialize();
            foreach (var query in ExactQueries)
            {
                browser.ShowFilter(query);
                browser.MatchCount.Should().Be(1);
                browser.IsSettingVisible("test.option").Should().BeTrue();
                browser.IsSettingVisible("test.option-extra").Should().BeFalse();
            }
            browser.Filter = "specific";
            Dispatcher.UIThread.RunJobs();
            browser.MatchCount.Should().Be(2);
        });
    }

    [Theory]
    [InlineData("TEST.OPTION", "")]
    [InlineData("test.other", "test.option")]
    public async Task DuplicateIdsAndAliasCollisionsFailExplicitly(string otherId, string otherAlias)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var first = new Border();
            var second = new Border();
            SettingsSearch.SetId(first, "test.option");
            SettingsSearch.SetId(second, otherId);
            SettingsSearch.SetAliases(second, otherAlias);
            using var browser = new SettingsBrowser
            {
                Items = { new SettingsPage { Header = "Test", Content = new StackPanel { Children = { first, second } } } },
            };
            FluentActions.Invoking(browser.Initialize).Should().Throw<InvalidOperationException>();
        });
    }

    [Theory]
    [InlineData("APPEARANCE.THEME", "appearance.theme")]
    [InlineData("reading text size", "appearance.reader-text-scale")]
    [InlineData("freeze presence particle", "appearance.reduced-motion")]
    [InlineData("speech.windows-rate", "speech.windows-rate")]
    public async Task SearchMatchesIdsTitlesAndDescriptionsAcrossPages(string query, string id)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                browser.SelectedItem.Should().BeNull();
                browser.MatchCount.Should().BeGreaterThan(50);
                browser.Filter = query;
                Dispatcher.UIThread.RunJobs();
                browser.IsSettingVisible(id).Should().BeTrue();
                browser.MatchCount.Should().Be(1);
                f.Main.IsVoiceEnabled.Should().BeFalse();
                f.Main.IsManualCallActive.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task HierarchyIntersectsSearchAndRetainsUnsavedValuesAndEditorInstances()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                var name = window.FindControl<SettingsPage>("SpeechAudioTab");
                f.Main.AssistantNameInput = "Unsaved Vega";
                browser.Filter = "Unsaved Vega";
                Dispatcher.UIThread.RunJobs();
                browser.IsSettingVisible("assistant.name").Should().BeTrue();
                browser.MatchCount.Should().Be(1);
                f.Main.AssistantNameInput = "Unsaved Orion";
                Dispatcher.UIThread.RunJobs();
                browser.MatchCount.Should().Be(0);

                browser.Filter = "speech";
                var all = browser.SectionTree.Items.Cast<TreeViewItem>().Single();
                var appearance = all.Items.Cast<TreeViewItem>().Single(item => item.Header is "Appearance");
                var presence = appearance.Items.Cast<TreeViewItem>().Single(item => item.Header is "Presence");
                browser.SectionTree.SelectedItem = presence;
                Dispatcher.UIThread.RunJobs();
                browser.IsSettingVisible("appearance.speech-scaling").Should().BeTrue();
                browser.IsSettingVisible("speech.windows-rate").Should().BeFalse();
                browser.IsSettingVisible("appearance.theme").Should().BeFalse();
                browser.SectionTree.SelectedItem = all;
                browser.IsSettingVisible("speech.windows-rate").Should().BeTrue();
                browser.Filter.Should().Be("speech");
                browser.Filter = string.Empty;
                Dispatcher.UIThread.RunJobs();
                browser.MatchCount.Should().BeGreaterThan(50);
                window.FindControl<SettingsPage>("SpeechAudioTab").Should().BeSameAs(name);
                f.Main.AssistantNameInput.Should().Be("Unsaved Orion");
                f.Main.AssistantName.Should().NotBe("Unsaved Orion");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task HierarchyHidesEmptySearchCategoriesWithoutHidingOtherMatchingSections()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                var all = browser.SectionTree.Items.Cast<TreeViewItem>().Single();
                var appearance = all.Items.Cast<TreeViewItem>().Single(item => item.Header is "Appearance");
                var speech = all.Items.Cast<TreeViewItem>().Single(item => item.Header is "Speech & audio");
                appearance.IsExpanded = true;
                speech.IsExpanded = true;
                var presence = appearance.Items.Cast<TreeViewItem>().Single(item => item.Header is "Presence");
                var output = speech.Items.Cast<TreeViewItem>().Single(item => item.Header is "Output");
                var setup = speech.Items.Cast<TreeViewItem>().Single(item => item.Header is "Setup");

                browser.Filter = "speech.windows-rate";
                Dispatcher.UIThread.RunJobs();
                browser.MatchCount.Should().Be(1);
                all.Items.Cast<TreeViewItem>().Where(item => item.IsEffectivelyVisible).Should().Equal(speech);
                speech.Items.Cast<TreeViewItem>().Where(item => item.IsEffectivelyVisible).Should().Equal(output);
                appearance.IsVisible.Should().BeFalse();
                setup.IsVisible.Should().BeFalse();

                browser.Filter = "speech";
                Dispatcher.UIThread.RunJobs();
                browser.SectionTree.SelectedItem = presence;
                Dispatcher.UIThread.RunJobs();
                browser.IsSettingVisible("speech.windows-rate").Should().BeFalse();
                speech.IsEffectivelyVisible.Should().BeTrue("matching alternatives must remain available outside the selected section");
                output.IsEffectivelyVisible.Should().BeTrue();
                browser.SectionTree.SelectedItem = output;
                browser.IsSettingVisible("speech.windows-rate").Should().BeTrue();
                presence.IsEffectivelyVisible.Should().BeTrue();
                browser.Filter.Should().Be("speech");

                browser.Filter = "no-matching-setting";
                Dispatcher.UIThread.RunJobs();
                browser.MatchCount.Should().Be(0);
                all.IsEffectivelyVisible.Should().BeTrue("All settings is the scope-recovery destination");
                all.Items.Cast<TreeViewItem>().Should().OnlyContain(item => !item.IsVisible);

                browser.Filter = string.Empty;
                Dispatcher.UIThread.RunJobs();
                all.Items.Cast<TreeViewItem>().Should().OnlyContain(item => item.IsVisible);
                speech.Items.Cast<TreeViewItem>().Should().OnlyContain(item => item.IsVisible);
                browser.SectionTree.SelectedItem.Should().BeSameAs(output);
                browser.SectionTree.SelectedItem = all;
                browser.MatchCount.Should().BeGreaterThan(50);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task HierarchyTracksDisplayedValueMatchesAndRestoresCategoriesAfterSearchClears()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                var all = browser.SectionTree.Items.Cast<TreeViewItem>().Single();
                var speech = all.Items.Cast<TreeViewItem>().Single(item => item.Header is "Speech & audio");
                speech.IsExpanded = true;
                var input = speech.Items.Cast<TreeViewItem>().Single(item => item.Header is "Input");
                f.Main.AssistantNameInput = "Unsaved Vega";
                browser.ShowFilter("Unsaved Vega");
                Dispatcher.UIThread.RunJobs();
                all.Items.Cast<TreeViewItem>().Where(item => item.IsEffectivelyVisible).Should().Equal(speech);
                speech.Items.Cast<TreeViewItem>().Where(item => item.IsEffectivelyVisible).Should().Equal(input);

                f.Main.AssistantNameInput = "Unsaved Orion";
                Dispatcher.UIThread.RunJobs();
                browser.MatchCount.Should().Be(0);
                speech.IsVisible.Should().BeFalse();

                browser.Filter = string.Empty;
                Dispatcher.UIThread.RunJobs();
                all.Items.Cast<TreeViewItem>().Should().OnlyContain(item => item.IsVisible);
                speech.Items.Cast<TreeViewItem>().Should().OnlyContain(item => item.IsVisible);
                f.Main.AssistantNameInput.Should().Be("Unsaved Orion");
                f.Main.AssistantName.Should().NotBe("Unsaved Orion");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NumericAndCompoundDisplayedDraftValuesAreSearchableWithoutSaving()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                var saved = f.Main.SavedPlaybackVolume;
                f.Main.SelectedPlaybackVolume = 73;
                browser.ShowFilter("73");
                browser.IsSettingVisible("speech.playback-volume").Should().BeTrue();
                f.Main.SavedPlaybackVolume.Should().Be(saved);
                f.Main.QuietHoursZone = "Antarctica/Troll";
                browser.ShowFilter("Antarctica/Troll");
                browser.IsSettingVisible("notices.quiet-hours").Should().BeTrue();
                browser.ShowFilter("no-matching-setting");
                browser.MatchCount.Should().Be(0);
                browser.ShowFilter("73");
                f.Main.SelectedPlaybackVolume.Should().Be(73);
                f.Main.QuietHoursZone.Should().Be("Antarctica/Troll");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task EveryRegisteredSettingCanBeFoundAndSearchNeverIndexesUndeclaredEditorText()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                foreach (var setting in RegisteredSettingRegistry.Settings)
                {
                    browser.ShowFilter(setting.Id);
                    browser.IsSettingVisible(setting.Id).Should().BeTrue(setting.Id + " must have an existing editor destination");
                }
                f.Main.SpeechComparisonPhrase = "not-a-setting-value";
                browser.ShowFilter("not-a-setting-value");
                browser.MatchCount.Should().Be(0);
                browser.ShowFilter("voice speed");
                browser.SearchField.Text.Should().Be("voice speed");
                browser.SelectedItem.Should().BeNull();
                browser.IsSettingVisible("speech.windows-rate").Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }
}
