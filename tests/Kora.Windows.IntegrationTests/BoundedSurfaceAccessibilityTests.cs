using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Presentation;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class BoundedSurfaceAccessibilityTests
{
    [Theory]
    [InlineData("5", "Response timeout in seconds")]
    [InlineData("10", "Presence timeout in seconds")]
    public async Task Timeout_labels_resolve_on_spinner_editor_and_step_buttons(string value, string label)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                window.UpdateLayout();
                var spinner = window.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(control => string.Equals(control.Text, value, StringComparison.Ordinal));
                ControlAutomationPeer.CreatePeerForElement(spinner)!.GetName().Should().Be(label);
                var editor = spinner.GetVisualDescendants().OfType<TextBox>().Single();
                ControlAutomationPeer.CreatePeerForElement(editor)!.GetName().Should().Be(label);
                var buttons = spinner.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Should().HaveCount(2);
                buttons.Select(button => ControlAutomationPeer.CreatePeerForElement(button)!.GetName())
                    .Should().BeEquivalentTo($"Increase {label}", $"Decrease {label}");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appearance_tab_cycle_reaches_all_inputs_and_returns_through_the_selected_tab(bool reverse)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var tab = (TabItem)window.FindControl<TabControl>("SettingsTabs")!.SelectedItem!;
                var first = ((Control)tab.Content!).GetVisualDescendants().OfType<ComboBox>().First();
                var cycle = ReadCycle(window, first, reverse);
                cycle.Select(control => ControlAutomationPeer.CreatePeerForElement(control)!.GetName())
                    .Should().BeEquivalentTo(
                        "Appearance", "Select an appearance option to inspect or change",
                        "Reset the selected appearance option to its default", "Show animated presence", "Application theme",
                        "Always show", "Stay on top", "Response timeout in seconds", "Presence timeout in seconds",
                        "Presence size in pixels", "Dot size percent", "Dot density percent", "Movement speed percent",
                        "Scale presence with speech playback", "Speech scale amount percent");
                cycle.Should().Contain(tab);
                ReadCycle(window, first, reverse).Should().Equal(cycle);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Package_header_arrow_selection_retains_a_complete_cycle_for_the_new_read_only_file()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(),
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var tabs = window.FindControl<TabControl>("SourceTabs")!;
                var first = (TabItem)tabs.SelectedItem!;
                first.Focus().Should().BeTrue();
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                tabs.SelectedIndex.Should().Be(1);
                window.UpdateLayout();
                var selected = (TabItem)tabs.SelectedItem!;
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(selected);
                var selector = window.FindControl<ComboBox>("PackageSelector")!;
                var reader = ((Control)selected.Content!).GetVisualDescendants().OfType<TextBox>().Single();
                ReadCycle(window, selector, reverse: false).Select(Describe)
                    .Should().Equal(new Control[] { selected, reader, selector }.Select(Describe));
                ReadCycle(window, selector, reverse: true).Select(Describe)
                    .Should().Equal(new Control[] { reader, selected, selector }.Select(Describe));
                reader.IsReadOnly.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Detail_escape_keeps_the_guide_alive_for_repeated_opening_and_other_bounded_windows()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
            var views = new List<DetailWindow>();
            using var details = new DetailWindowController(fixture.Documents, () => fixture.Access.Open,
                renderer, new RejectingClipboard(), (state, result, copy) =>
                {
                    var view = new DetailWindow(state, result, copy);
                    views.Add(view);
                    return view;
                }, NullLogger<DetailWindowController>.Instance);
            var guide = new DocumentationWindow(fixture.Documents, fixture.Main, details);
            var settings = new SettingsWindow(fixture.Main);
            var packages = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(), renderer);
            try
            {
                guide.Show();
                for (var index = 0; index < 3; index++)
                {
                    guide.Activate();
                    guide.FindControl<Button>("OpenDetails")!.Focus().Should().BeTrue();
                    guide.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    var detail = views[index];
                    detail.IsVisible.Should().BeTrue();
                    detail.Owner.Should().BeSameAs(guide);
                    detail.Activate();
                    detail.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    detail.IsVisible.Should().BeFalse();
                    detail.FindControl<TextBlock>("ProvenanceLabel")!.Text.Should().BeNullOrEmpty();
                    guide.IsVisible.Should().BeTrue();
                    guide.DataContext.Should().BeSameAs(fixture.Main);
                    settings.Show();
                    packages.Show();
                    guide.IsVisible.Should().BeTrue();
                    settings.Hide();
                    packages.Hide();
                }
            }
            finally
            {
                packages.Close();
                settings.Close();
                guide.Close();
            }
        });
    }

    private sealed class RejectingClipboard : IDetailClipboard
    {
        public Task WritePlainTextAsync(IDetailView owner, string source) =>
            throw new InvalidOperationException("Clipboard operations are not authorized by this accessibility test.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Package_tab_cycle_includes_selector_selected_file_and_read_only_source(bool reverse)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(),
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var selector = window.FindControl<ComboBox>("PackageSelector")!;
                var tab = (TabItem)window.FindControl<TabControl>("SourceTabs")!.SelectedItem!;
                var source = ((Control)tab.Content!).GetVisualDescendants().OfType<TextBox>().Single();
                var expected = reverse ? new Control[] { source, tab, selector } : [tab, source, selector];
                ReadCycle(window, selector, reverse).Select(Describe).Should().Equal(expected.Select(Describe));
                ReadCycle(window, selector, reverse).Select(Describe).Should().Equal(expected.Select(Describe));
                source.IsReadOnly.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    private static List<Control> ReadCycle(Window window, Control first, bool reverse)
    {
        first.Focus().Should().BeTrue();
        var cycle = new List<Control>();
        var previous = first;
        for (var index = 0; index < 40; index++)
        {
            window.KeyPress(Key.Tab, reverse ? RawInputModifiers.Shift : RawInputModifiers.None, PhysicalKey.Tab, "Tab");
            var focused = window.FocusManager!.GetFocusedElement();
            focused.Should().BeAssignableTo<Control>();
            var control = (Control)focused!;
            ReferenceEquals(control, previous).Should().BeFalse("Tab must move, including at the selected content boundary: "
                + string.Join(" -> ", control.GetVisualAncestors().Prepend(control).OfType<Control>()
                    .Select(parent => $"{parent.GetType().Name}#{parent.Name}:{KeyboardNavigation.GetTabNavigation(parent)}"
                        + $"/index={parent.TabIndex}/stop={parent.IsTabStop}"
                        + $"/active={KeyboardNavigation.GetTabOnceActiveElement(parent)}"))
                + "; tab tree: " + string.Join(", ", window.GetVisualDescendants().OfType<Control>()
                    .Where(item => item is TabItem or Avalonia.Controls.Presenters.ItemsPresenter)
                    .Select(item => $"{Describe(item)}:{KeyboardNavigation.GetTabNavigation(item)}/index={item.TabIndex}/stop={item.IsTabStop}")));
            cycle.Add(control);
            if (ReferenceEquals(control, first)) { return cycle; }
            cycle.Should().OnlyHaveUniqueItems("focus must not loop within a nested reader instead of returning to the selector");
            previous = control;
        }
        throw new InvalidOperationException("The bounded window did not complete its Tab cycle within 40 steps.");
    }

    private static string Describe(Control control) =>
        $"{control.GetType().Name}#{control.Name}:{ControlAutomationPeer.CreatePeerForElement(control)?.GetName()}";
}
