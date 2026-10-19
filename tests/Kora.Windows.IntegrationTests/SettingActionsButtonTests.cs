using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingActionsButtonTests
{
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
                var icon = actions.Content.Should().BeOfType<PathIcon>().Which;
                icon.Bounds.Size.Should().Be(new Size(16, 16));
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
}
