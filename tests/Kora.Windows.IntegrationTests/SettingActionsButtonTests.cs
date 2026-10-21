using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingActionsButtonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cog_is_unboxed_until_hover_and_preserves_its_hit_target_through_pressed_and_open_states(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var actions = new SettingActionsButton { SettingName = "Response timeout" };
            var window = StyledWindow(actions, dark);
            try
            {
                window.Show();
                window.MouseMove(new Point(1, 1));
                window.UpdateLayout();
                var presenter = Presenter(actions);
                var bounds = actions.Bounds;
                bounds.Width.Should().Be(32);
                bounds.Height.Should().Be(32);
                var icon = actions.Content.Should().BeOfType<Avalonia.Controls.Shapes.Path>().Which;
                icon.Width.Should().Be(14);
                icon.Height.Should().Be(14);
                icon.Fill.Should().BeNull();
                icon.StrokeThickness.Should().Be(1.2);
                icon.StrokeJoin.Should().Be(PenLineJoin.Round);
                BrushColor(icon.Stroke).Should().Be(ResourceColor(window, "KoraMutedForegroundBrush"));
                BrushColor(presenter.Background).Should().Be(Colors.Transparent);
                BrushColor(presenter.BorderBrush).Should().Be(Colors.Transparent);
                BrushColor(actions.Foreground).Should().Be(ResourceColor(window, "KoraMutedForegroundBrush"));
                actions.CornerRadius.Should().Be(new CornerRadius(4));
                presenter.BorderThickness.Should().Be(new Thickness(1));
                var point = actions.TranslatePoint(new Point(16, 16), window)
                    ?? throw new InvalidOperationException("The actions button is not attached to its window.");

                window.MouseMove(point);
                window.UpdateLayout();
                BrushColor(presenter.Background).Should().Be(ResourceColor(window, "KoraSurfaceAltBrush"));
                BrushColor(presenter.BorderBrush).Should().Be(ResourceColor(window, "KoraBorderBrush"));
                BrushColor(actions.Foreground).Should().Be(ResourceColor(window, "KoraForegroundBrush"));
                BrushColor(icon.Stroke).Should().Be(ResourceColor(window, "KoraForegroundBrush"));
                actions.Bounds.Should().Be(bounds);

                window.MouseDown(point, MouseButton.Left);
                window.UpdateLayout();
                actions.IsPressed.Should().BeTrue();
                BrushColor(presenter.BorderBrush).Should().Be(ResourceColor(window, "KoraMutedForegroundBrush"));
                window.MouseUp(point, MouseButton.Left);
                window.MouseMove(new Point(1, 1));
                window.UpdateLayout();
                var flyout = (MenuFlyout)actions.Flyout!;
                flyout.IsOpen.Should().BeTrue();
                BrushColor(presenter.BorderBrush).Should().Be(ResourceColor(window, "KoraBorderBrush"));
                actions.Bounds.Should().Be(bounds);

                flyout.Hide();
                window.UpdateLayout();
                BrushColor(presenter.Background).Should().Be(Colors.Transparent);
                BrushColor(presenter.BorderBrush).Should().Be(Colors.Transparent);
                actions.Bounds.Should().Be(bounds);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keyboard_focus_has_a_visible_outline_and_space_opens_the_same_menu_without_resetting(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var resetCount = 0;
            var actions = new SettingActionsButton
            {
                SettingName = "Response timeout",
                ResetCommand = new AsyncCommand(
                    () => { resetCount++; return Task.CompletedTask; },
                    exception => throw new InvalidOperationException("The reset failed.", exception)),
            };
            var window = StyledWindow(actions, dark);
            try
            {
                window.Show();
                window.MouseMove(new Point(1, 1));
                actions.Focus(NavigationMethod.Tab).Should().BeTrue();
                window.UpdateLayout();
                actions.IsFocused.Should().BeTrue();
                BrushColor(Presenter(actions).BorderBrush).Should().Be(ResourceColor(window, "KoraAccentBrush"));
                ControlAutomationPeer.CreatePeerForElement(actions)!.GetName()
                    .Should().Be("Response timeout actions");

                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.UpdateLayout();
                var flyout = (MenuFlyout)actions.Flyout!;
                flyout.IsOpen.Should().BeTrue();
                resetCount.Should().Be(0);
                flyout.Items.OfType<MenuItem>().Single().Command.Should().BeSameAs(actions.ResetCommand);
                flyout.Hide();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keyboard_opening_an_inspection_menu_does_nothing_and_Refresh_runs_only_its_admitted_command(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var refreshCount = 0;
            var resetCount = 0;
            var admitted = true;
            var refreshCommand = new AsyncCommand(
                () => { refreshCount++; return Task.CompletedTask; },
                exception => throw new InvalidOperationException("Refresh failed.", exception), () => admitted);
            var refresh = new MenuItem { Header = "Refresh", Command = refreshCommand };
            var reset = new MenuItem
            {
                Header = "Reset setting",
                IsEnabled = false,
                Command = new AsyncCommand(
                    () => { resetCount++; return Task.CompletedTask; },
                    exception => throw new InvalidOperationException("Reset failed.", exception)),
            };
            var flyout = new MenuFlyout { Items = { refresh, new Separator(), reset } };
            var actions = new SettingActionsButton { SettingName = "Voice speed", Flyout = flyout };
            var window = StyledWindow(actions, dark);
            try
            {
                window.Show();
                actions.Focus(NavigationMethod.Tab).Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.UpdateLayout();
                flyout.IsOpen.Should().BeTrue();
                refresh.IsEffectivelyEnabled.Should().BeTrue();
                reset.IsEffectivelyEnabled.Should().BeFalse();
                refreshCount.Should().Be(0);
                resetCount.Should().Be(0);

                ClickMenuItem(refresh);
                refreshCount.Should().Be(1);
                resetCount.Should().Be(0);
                flyout.IsOpen.Should().BeFalse();

                admitted = false;
                refreshCommand.NotifyCanExecuteChanged();
                flyout.ShowAt(actions);
                window.UpdateLayout();
                refresh.IsEffectivelyEnabled.Should().BeFalse();
                ClickMenuItem(refresh);
                refreshCount.Should().Be(1);
                resetCount.Should().Be(0);
                flyout.Hide();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_cog_is_subdued_and_does_not_gain_hover_chrome_or_open_its_menu(bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var actions = new SettingActionsButton { SettingName = "Response timeout", IsEnabled = false };
            var window = StyledWindow(actions, dark);
            try
            {
                window.Show();
                window.UpdateLayout();
                var point = actions.TranslatePoint(new Point(16, 16), window)
                    ?? throw new InvalidOperationException("The actions button is not attached to its window.");
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout();

                actions.Opacity.Should().Be(0.45);
                BrushColor(Presenter(actions).Background).Should().Be(Colors.Transparent);
                BrushColor(Presenter(actions).BorderBrush).Should().Be(Colors.Transparent);
                ((MenuFlyout)actions.Flyout!).IsOpen.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(360, false)]
    [InlineData(700, false)]
    [InlineData(360, true)]
    [InlineData(700, true)]
    public async Task Cog_respects_card_padding_at_the_top_right(int width, bool dark)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var actions = new SettingActionsButton { SettingName = "Response timeout" };
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 24,
                MinHeight = 96,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Hide an unpinned response window after this many seconds without interaction.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    actions,
                },
            };
            Grid.SetColumn(actions, 1);
            var card = new Border
            {
                Padding = new Thickness(18),
                BorderThickness = new Thickness(1),
                Child = grid,
            };
            var window = new Window
            {
                Width = width,
                Height = 180,
                Content = card,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                var topLeft = actions.TranslatePoint(default, card)
                    ?? throw new InvalidOperationException("The actions button is not attached to its card.");
                topLeft.Y.Should().BeApproximately(19, 0.1);
                (topLeft.X + actions.Bounds.Width).Should().BeApproximately(card.Bounds.Width - 19, 0.1);
                actions.Bounds.Size.Should().Be(new Size(32, 32));
                actions.Padding.Should().Be(new Thickness(6));
                var icon = actions.Content.Should().BeOfType<Avalonia.Controls.Shapes.Path>().Which;
                icon.Bounds.Size.Should().Be(new Size(14, 14));
                icon.Data!.FillContains(new Point(9, 9)).Should().BeFalse();
                icon.Data.FillContains(new Point(9, 2)).Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(actions)!.GetName()
                    .Should().Be("Response timeout actions");
                ToolTip.GetTip(actions).Should().Be("Response timeout actions");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clicking_the_cog_only_opens_the_menu_and_preserves_reset_availability(bool canReset)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var resetCount = 0;
            var reset = new AsyncCommand(
                () => { resetCount++; return Task.CompletedTask; },
                exception => throw new InvalidOperationException("The reset failed.", exception),
                () => canReset);
            var actions = new SettingActionsButton
            {
                SettingName = "Response timeout",
                ResetCommand = reset,
            };
            var window = new Window
            {
                Width = 300,
                Height = 150,
                Content = new Border { Padding = new Thickness(18), Child = actions },
            };
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                var point = actions.TranslatePoint(new Point(16, 16), window)
                    ?? throw new InvalidOperationException("The actions button is not attached to its window.");

                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout();

                var flyout = (MenuFlyout)actions.Flyout!;
                flyout.IsOpen.Should().BeTrue();
                resetCount.Should().Be(0);
                var item = flyout.Items.OfType<MenuItem>().Single();
                item.Header.Should().Be("Reset setting");
                item.Command.Should().BeSameAs(reset);
                item.IsEffectivelyEnabled.Should().Be(canReset);
                ControlAutomationPeer.CreatePeerForElement(item)!.GetName()
                    .Should().Be("Reset Response timeout to its default");
                flyout.Hide();
            }
            finally { window.Close(); }
        });
    }

    private static Window StyledWindow(Control content, bool dark)
    {
        var window = new Window
        {
            Width = 300,
            Height = 150,
            Content = new Border { Padding = new Thickness(18), Child = content },
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Kora/"))
        {
            Source = new Uri("avares://Kora/Assets/ThemeResources.axaml"),
        });
        window.Styles.Add(new StyleInclude(new Uri("avares://Kora/"))
        {
            Source = new Uri("avares://Kora/Controls/SettingActionsButton.axaml"),
        });
        window.Styles.Add(new StyleInclude(new Uri("avares://Kora/"))
        {
            Source = new Uri("avares://Kora/Controls/SettingsEditorStyles.axaml"),
        });
        return window;
    }

    private static ContentPresenter Presenter(SettingActionsButton actions) =>
        actions.GetVisualDescendants().OfType<ContentPresenter>().Single(presenter =>
            string.Equals(presenter.Name, "PART_ContentPresenter", StringComparison.Ordinal));

    private static void ClickMenuItem(MenuItem item)
    {
        var topLevel = TopLevel.GetTopLevel(item)
            ?? throw new InvalidOperationException("The menu item has no input surface.");
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), topLevel)
            ?? throw new InvalidOperationException("The menu item is not attached to its input surface.");
        topLevel.MouseMove(point);
        topLevel.MouseDown(point, MouseButton.Left);
        topLevel.MouseUp(point, MouseButton.Left);
    }

    private static Color ResourceColor(Window window, string key)
    {
        window.Resources.TryGetResource(key, window.ActualThemeVariant, out var resource).Should().BeTrue();
        return resource.Should().BeAssignableTo<ISolidColorBrush>().Which.Color;
    }

    private static Color BrushColor(IBrush? brush) =>
        brush.Should().BeAssignableTo<ISolidColorBrush>().Which.Color;
}
