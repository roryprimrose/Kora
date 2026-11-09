using Avalonia.Interactivity;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class SettingsView
{
    private void OnReviewHandoffClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel main) { main.ReviewPendingHandoff(); }
    }
}
