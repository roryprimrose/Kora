using Avalonia.Interactivity;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class SettingsWindow
{
    private void OnReviewHandoffClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel main) { main.ReviewPendingHandoff(); }
    }
}
