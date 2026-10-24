using Avalonia.Interactivity;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class SettingsWindow
{
    private void OnCopilotAccountClicked(object? sender, RoutedEventArgs args) =>
        (DataContext as MainViewModel)?.ConfigureCopilotAccount();
}
