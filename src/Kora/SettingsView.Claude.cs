using Avalonia.Interactivity;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class SettingsView
{
    private void OnClaudeAccountClicked(object? sender, RoutedEventArgs args) =>
        (DataContext as MainViewModel)?.ConfigureClaudeAccount();
}
