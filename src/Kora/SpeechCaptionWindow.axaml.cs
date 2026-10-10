using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SpeechCaptionWindow : Window
{
    public SpeechCaptionWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    public SpeechCaptionWindow(ISpeechCaptionPresentation viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    internal void BindDisplaySelection(SpeechCaptionWindowController controller) =>
        this.FindControl<CaptionDisplaySelector>("CaptionDisplaySelector")!.Bind(controller);
}
