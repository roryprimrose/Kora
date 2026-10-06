using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Kora.Theming;

namespace Kora.Setup;

internal sealed class SetupApplication(Func<SetupWindow> createWindow, ThemeVariant? previewTheme = null) : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = previewTheme ?? ThemeVariant.Default;
        Resources.MergedDictionaries.Add(new ThemeResources());
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = createWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
