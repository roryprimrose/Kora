using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ButtonThemeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ApplicationButtonsAreSlimFlatAndKeepTheirKeyboardCommand(bool dark, bool primary) =>
        HeadlessSession.RunAsync(() =>
        {
            var clicks = 0;
            var button = new Button { Content = "Confirm selection" };
            if (primary) { button.Classes.Add("primary"); }
            button.Click += (_, _) => clicks++;
            AutomationProperties.SetName(button, "Confirm selection");
            var window = StyledWindow(button, dark);
            try
            {
                window.Show();
                window.MouseMove(new Point(1, 1));
                Render(window);
                button.Bounds.Height.Should().Be(28);
                button.Padding.Should().Be(new Thickness(8, 3));
                button.CornerRadius.Should().Be(new CornerRadius(3));
                button.BorderThickness.Should().Be(new Thickness(1));
                button.FontSize.Should().Be(13);
                button.FontWeight.Should().Be(FontWeight.Normal);
                var presenter = Presenter(button);
                ColorOf(presenter.Background).Should().Be(primary
                    ? ResourceColor(window, "KoraPrimaryBrush") : Colors.Transparent);
                var bounds = button.Bounds;
                var point = button.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), window)!.Value;

                window.MouseMove(point);
                Render(window);
                ColorOf(presenter.Background).Should().Be(ResourceColor(window,
                    primary ? "KoraAccentBrush" : "KoraSurfaceAltBrush"));
                ColorOf(presenter.Foreground).Should().Be(ResourceColor(window,
                    primary ? "KoraAccentForegroundBrush" : "KoraForegroundBrush"));
                window.MouseDown(point, MouseButton.Left);
                Render(window);
                ColorOf(presenter.Background).Should().Be(ResourceColor(window,
                    primary ? "KoraPrimaryBrush" : "KoraBorderBrush"));
                button.Bounds.Should().Be(bounds);
                window.MouseUp(point, MouseButton.Left);
                window.MouseMove(new Point(1, 1));
                window.FocusManager!.Focus(null);
                button.Focus(NavigationMethod.Tab).Should().BeTrue();
                Render(window);
                ColorOf(presenter.BorderBrush).Should().Be(ResourceColor(window,
                    primary ? "KoraAccentForegroundBrush" : "KoraAccentBrush"));
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Be("Confirm selection");
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                clicks.Should().Be(2);
                button.IsEnabled = true;
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                Render(window);
                ColorOf(presenter.Background).Should().Be(primary
                    ? ResourceColor(window, "KoraPrimaryBrush") : Colors.Transparent);
                ColorOf(presenter.Foreground).Should().Be(ResourceColor(window,
                    primary ? "KoraAccentForegroundBrush" : "KoraForegroundBrush"));
                button.Bounds.Should().Be(bounds);
                button.IsEnabled = false;
                Render(window);
                button.Opacity.Should().Be(0.45);
                button.Bounds.Should().Be(bounds);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                clicks.Should().Be(2);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false, "response-link")]
    [InlineData(true, "response-link")]
    [InlineData(false, "toolbar")]
    [InlineData(true, "toolbar")]
    [InlineData(false, "pending")]
    [InlineData(true, "pending")]
    public Task SupportingButtonVariantsKeepFlatStatesAndVisibleFocus(bool dark, string variant) =>
        HeadlessSession.RunAsync(() =>
        {
            var button = new Button { Content = "Review choice" };
            button.Classes.Add(variant);
            var window = StyledWindow(button, dark);
            try
            {
                window.Show();
                window.MouseMove(new Point(1, 1));
                Render(window);
                var bounds = button.Bounds;
                bounds.Height.Should().Be(28);
                var presenter = Presenter(button);
                ColorOf(presenter.Background).Should().Be(variant is "pending"
                    ? ResourceColor(window, "KoraPrimaryBrush") : Colors.Transparent);
                var point = button.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Render(window);
                ColorOf(presenter.Background).Should().Be(variant switch
                {
                    "response-link" => Colors.Transparent,
                    "pending" => ResourceColor(window, "KoraAccentBrush"),
                    _ => ResourceColor(window, "KoraSurfaceAltBrush"),
                });
                ColorOf(presenter.BorderBrush).Should().Be(Colors.Transparent);
                window.MouseMove(new Point(1, 1));
                button.Focus(NavigationMethod.Tab).Should().BeTrue();
                Render(window);
                ColorOf(presenter.BorderBrush).Should().Be(ResourceColor(window,
                    variant is "pending" ? "KoraAccentForegroundBrush" : "KoraAccentBrush"));
                button.Bounds.Should().Be(bounds);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompactGlyphButtonsAreUnboxedAndKeyboardOverflowStillWorks(bool dark) =>
        HeadlessSession.RunAsync(() =>
        {
            var button = new Button
            {
                Classes = { "compact-action" },
                Content = new PathIcon { Data = Geometry.Parse("M 2,2 L 14,2 L 14,14 L 2,14 Z") },
                Flyout = new MenuFlyout { Items = { new MenuItem { Header = "Current response details" } } },
            };
            AutomationProperties.SetName(button, "More response actions");
            ToolTip.SetTip(button, "More response actions");
            var window = StyledWindow(button, dark);
            try
            {
                window.Show();
                window.MouseMove(new Point(1, 1));
                Render(window);
                button.Bounds.Size.Should().Be(new Size(28, 28));
                var presenter = Presenter(button);
                ColorOf(presenter.Background).Should().Be(Colors.Transparent);
                ColorOf(presenter.BorderBrush).Should().Be(Colors.Transparent);
                ((PathIcon)button.Content).Bounds.Size.Should().Be(new Size(16, 16));
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Be("More response actions");
                ToolTip.GetTip(button).Should().Be("More response actions");
                button.Focus(NavigationMethod.Tab).Should().BeTrue();
                Render(window);
                ColorOf(presenter.BorderBrush).Should().Be(ResourceColor(window, "KoraAccentBrush"));
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Render(window);
                button.Flyout.IsOpen.Should().BeTrue();
                button.Flyout.Hide();
            }
            finally { window.Close(); }
        });

    private static Window StyledWindow(Button button, bool dark)
    {
        var window = new Window
        {
            Width = 400,
            Height = 160,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Margin = new Thickness(24), Children = { button } },
        };
        var presentation = new Avalonia.Application();
        NativeUxFixtureHost.LoadPresentation(presentation);
        var resources = presentation.Resources;
        presentation.Resources = new ResourceDictionary();
        window.Resources = resources;
        var styles = presentation.Styles.ToArray();
        presentation.Styles.Clear();
        foreach (var style in styles) { window.Styles.Add(style); }
        return window;
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();
        using (window.CaptureRenderedFrame()) { }
    }

    private static ContentPresenter Presenter(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().Single(presenter =>
            string.Equals(presenter.Name, "PART_ContentPresenter", StringComparison.Ordinal));

    private static Color ResourceColor(Window window, string key)
    {
        window.TryGetResource(key, window.ActualThemeVariant, out var resource).Should().BeTrue();
        return ColorOf(resource.Should().BeAssignableTo<IBrush>().Which);
    }

    private static Color ColorOf(IBrush? brush) =>
        brush.Should().BeAssignableTo<ISolidColorBrush>().Which.Color;
}
