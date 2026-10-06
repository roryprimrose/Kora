using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AwesomeAssertions;
using Kora.Theming;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerThemeTests
{
    [Theory]
    [InlineData(false, "#F4F5F8", "#24202F")]
    [InlineData(true, "#101216", "#E8ECF2")]
    public void SharedThemeResourcesHaveTheKoraPaletteAndReadableSetupText(bool dark, string background, string foreground)
    {
        var dictionary = new ThemeResources();
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var backdrop = Brush(dictionary, variant, "KoraWindowBackgroundBrush");
        backdrop.Should().Be(Color.Parse(background));
        Brush(dictionary, variant, "KoraForegroundBrush").Should().Be(Color.Parse(foreground));
        foreach (var key in new[] { "KoraForegroundBrush", "KoraMutedForegroundBrush", "KoraStatusBrush", "KoraWarningBrush" })
        {
            var text = Brush(dictionary, variant, key);
            var first = Luminance(text);
            var second = Luminance(backdrop);
            ((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05))
                .Should().BeGreaterThanOrEqualTo(4.5, $"{key} must be readable in {variant} mode");
        }
    }

    private static Color Brush(ResourceDictionary dictionary, ThemeVariant variant, string key)
    {
        if (!dictionary.TryGetResource(key, variant, out var resource) || resource is not ISolidColorBrush brush)
        {
            throw new InvalidOperationException($"Missing theme brush: {key}");
        }
        return brush.Color;
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
