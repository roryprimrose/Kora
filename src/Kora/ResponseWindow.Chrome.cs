using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Kora;

public sealed partial class ResponseWindow
{
    private void ConfigureChrome(bool expanded)
    {
        Classes.Set("compact", !expanded);
        CollapseWorkbench.IsVisible = expanded;
        CanMaximize = expanded;
        MaximizeWorkbench.IsEnabled = expanded;
        UpdateActivityNavigation();
        UpdateCaptionState();
    }

    private void UpdateCaptionState()
    {
        var maximized = WindowState == WindowState.Maximized;
        var label = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeWorkbench, label);
        ToolTip.SetTip(MaximizeWorkbench, label);
        MaximizeGlyph.Data = Geometry.Parse(maximized
            ? "M 2,0 L 10,0 L 10,8 M 0,2 L 8,2 L 8,10 L 0,10 Z"
            : "M 0,0 L 10,0 L 10,10 L 0,10 Z");
    }

    private void OnMinimizeWorkbench(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void OnMaximizeWorkbench(object? sender, RoutedEventArgs args)
    {
        if (CanMaximize)
        { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    }
    private void OnCloseWorkbench(object? sender, RoutedEventArgs args) => Close();

    private void RetireMinimizedPresentation()
    {
        sessionPresentationEpoch++;
        SessionWorkspace.HideDetails();
        viewModel.RetireCurrentResponseSpeech();
        pushToTalk.Release();
        OnReviewedCommandUnavailable(this, EventArgs.Empty);
    }
}
