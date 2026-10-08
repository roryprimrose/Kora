using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kora.Application.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SpeechCaptionWindow : Window
{
    public SpeechCaptionWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    public SpeechCaptionWindow(MainViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }
}
