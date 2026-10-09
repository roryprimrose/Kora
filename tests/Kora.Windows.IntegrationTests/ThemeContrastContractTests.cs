using System.Globalization;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Static WCAG 2.x contrast-ratio checks on the bundled Light/Dark palettes in
/// <c>ThemeResources.axaml</c>. These are read-only textual checks (no rendering),
/// pinning the already-shipped colour pairs so a future palette edit cannot silently
/// regress text or status legibility below the thresholds the product already meets.
/// </summary>
public sealed class ThemeContrastContractTests
{
    private const double NormalTextMinimumRatio = 4.5;

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Body_text_meets_normal_text_contrast_against_its_window_and_surface_backgrounds(string themeKey)
    {
        var brushes = LoadThemeBrushes(themeKey);
        AssertRatioAtLeast(brushes, "KoraForegroundBrush", "KoraWindowBackgroundBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraForegroundBrush", "KoraSurfaceBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraForegroundBrush", "KoraSurfaceAltBrush", NormalTextMinimumRatio);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Muted_text_meets_normal_text_contrast_against_its_backgrounds(string themeKey)
    {
        var brushes = LoadThemeBrushes(themeKey);
        AssertRatioAtLeast(brushes, "KoraMutedForegroundBrush", "KoraWindowBackgroundBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraMutedForegroundBrush", "KoraSurfaceBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraMutedForegroundBrush", "KoraSurfaceAltBrush", NormalTextMinimumRatio);
    }

    [Fact]
    public void Light_secondary_text_is_darker_than_the_previous_palette()
    {
        var brushes = LoadThemeBrushes("Light");
        RelativeLuminance(brushes["KoraMutedForegroundBrush"])
            .Should().BeLessThan(RelativeLuminance(ParseHexColor("#5E6470")));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Accent_foreground_meets_normal_text_contrast_against_the_accent_and_primary_brushes(string themeKey)
    {
        var brushes = LoadThemeBrushes(themeKey);
        AssertRatioAtLeast(brushes, "KoraAccentForegroundBrush", "KoraAccentBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraAccentForegroundBrush", "KoraPrimaryBrush", NormalTextMinimumRatio);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Status_and_warning_text_meet_normal_text_contrast_against_window_background(string themeKey)
    {
        var brushes = LoadThemeBrushes(themeKey);
        AssertRatioAtLeast(brushes, "KoraStatusBrush", "KoraWindowBackgroundBrush", NormalTextMinimumRatio);
        AssertRatioAtLeast(brushes, "KoraWarningBrush", "KoraWindowBackgroundBrush", NormalTextMinimumRatio);
    }

    // These assertions cover opaque declared text pairs only, not rendered states, focus indicators,
    // gradients or component boundaries. Each non-text exemption requires its own usage assessment.

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#24202F", "#24202F", 1.0)]
    [InlineData("#FF24202F", "#24202F", 1.0)]
    public void Contrast_measurement_matches_reference_pairs(string foreground, string background, double expected) =>
        ContrastRatio(ParseHexColor(foreground), ParseHexColor(background)).Should().BeApproximately(expected, 0.000001);

    [Theory]
    [InlineData("#F2181C23")]
    [InlineData("#123")]
    [InlineData("FFFFFF")]
    public void Uncomposited_transparency_and_unsupported_colour_forms_are_refused(string value)
    {
        var parse = () => ParseHexColor(value);
        parse.Should().Throw<InvalidDataException>();
    }

    private static void AssertRatioAtLeast(IReadOnlyDictionary<string, (byte R, byte G, byte B)> brushes, string foregroundKey, string backgroundKey, double minimum)
    {
        var ratio = ContrastRatio(brushes[foregroundKey], brushes[backgroundKey]);
        ratio.Should().BeGreaterThanOrEqualTo(minimum,
            $"{foregroundKey} on {backgroundKey} must remain readable at WCAG {minimum:0.0}:1 (actual {ratio:0.00}:1)");
    }

    private static double ContrastRatio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        var darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Dictionary<string, (byte R, byte G, byte B)> LoadThemeBrushes(string themeKey)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "Assets", "ThemeResources.axaml"));
        XNamespace ns = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var themeDictionary = document.Descendants(ns + "ResourceDictionary")
            .Single(element => string.Equals(element.Attribute(x + "Key")?.Value, themeKey, StringComparison.Ordinal));

        var brushes = new Dictionary<string, (byte, byte, byte)>(StringComparer.Ordinal);
        foreach (var brush in themeDictionary.Elements(ns + "SolidColorBrush"))
        {
            var key = brush.Attribute(x + "Key")!.Value;
            var color = brush.Attribute("Color")!.Value;
            if (key is "KoraForegroundBrush" or "KoraMutedForegroundBrush" or "KoraWindowBackgroundBrush"
                or "KoraSurfaceBrush" or "KoraSurfaceAltBrush" or "KoraAccentForegroundBrush"
                or "KoraAccentBrush" or "KoraPrimaryBrush" or "KoraStatusBrush" or "KoraWarningBrush")
            {
                brushes.Add(key, ParseHexColor(color));
            }
        }

        return brushes;
    }

    private static (byte R, byte G, byte B) ParseHexColor(string value)
    {
        if (!value.StartsWith('#') || value.Length is not (7 or 9)
            || (value.Length == 9 && !value.AsSpan(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Contrast measurement requires an opaque #RRGGBB or #FFRRGGBB colour.");
        }
        var hex = value[1..];
        var rgb = hex.Length == 8 ? hex[2..] : hex;
        var r = byte.Parse(rgb[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(rgb[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(rgb[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (r, g, b);
    }
}
