using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Kora.Windows.IntegrationTests.HeadlessTestApp))]

namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Minimal, test-only Avalonia application used solely to render already-delivered native windows
/// off-screen for runtime accessibility measurement (automation names, keyboard focus, DPI scaling).
/// This deliberately never runs Kora's real composition root (<c>Program.cs</c>/<c>App.axaml.cs</c>),
/// so instance-ownership coordination, privacy/consent gating and device-local settings persistence
/// never execute. Only the stock FluentTheme is loaded; Kora's own <c>{DynamicResource}</c> palette
/// brushes resolve to Avalonia's unset/fallback values here, which is acceptable because these
/// runtime checks assert automation-tree names, keyboard focus reachability and DPI-scale rendering
/// stability, not actual pixel colour values (contrast ratios are verified separately, statically,
/// against the theme's declared hex values without needing to render anything).
/// </summary>
public sealed class HeadlessTestApp : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    /// <summary>
    /// Entry point resolved by <see cref="AvaloniaTestApplicationAttribute"/>. Headless drawing must
    /// be disabled (and a real Skia backend selected) so that <c>CaptureRenderedFrame</c> produces
    /// actual pixel output instead of Avalonia's default no-op headless renderer; the default headless
    /// options skip rendering entirely for speed, which is unsuitable for DPI/contrast verification.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
    }
}
