using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public SettingsWindow(MainViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }
}
