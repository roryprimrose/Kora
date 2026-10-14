using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SpeechCaptionWindow : Window
{
    private readonly TextBox captionText;
    private readonly Action<int> scaleCaption;
    private int captionPercent = ReaderTextScaleSettings.DefaultPercent;

    public SpeechCaptionWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    public SpeechCaptionWindow(ISpeechCaptionPresentation viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        captionText = this.FindControl<TextBox>("CaptionText")!;
        scaleCaption = NativeReaderTypography.Capture(captionText);
        Closed += (_, _) => ClearCaption();
    }

    internal void ApplyCaption(string text, int percent)
    {
        scaleCaption(percent);
        captionPercent = percent;
        captionText.Text = text;
    }

    internal void ClearCaption()
    {
        captionText.Text = null;
        scaleCaption(ReaderTextScaleSettings.DefaultPercent);
        captionPercent = ReaderTextScaleSettings.DefaultPercent;
    }

    internal Size MeasureOriginalContent(Size available)
    {
        var content = Content as Control
            ?? throw new InvalidOperationException("The caption native surface is unavailable.");
        // Reader scaling changes reflow inside the existing viewport, not native placement/geometry.
        scaleCaption(ReaderTextScaleSettings.DefaultPercent);
        try
        {
            content.Measure(available);
            return content.DesiredSize;
        }
        finally { scaleCaption(captionPercent); }
    }

    internal void BindDisplaySelection(SpeechCaptionWindowController controller) =>
        this.FindControl<CaptionDisplaySelector>("CaptionDisplaySelector")!.Bind(controller);
}
