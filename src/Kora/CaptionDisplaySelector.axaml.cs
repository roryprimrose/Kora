using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Kora;

public sealed partial class CaptionDisplaySelector : UserControl
{
    private Window? owner;

    public CaptionDisplaySelector()
    {
        AvaloniaXamlLoader.Load(this);
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    internal void Bind(SpeechCaptionWindowController controller) => DataContext = controller;
    private void OnSelect(object? sender, RoutedEventArgs args) => Select(reset: false);
    private void OnReset(object? sender, RoutedEventArgs args) => Select(reset: true);

    private void Select(bool reset)
    {
        if (DataContext is SpeechCaptionWindowController controller && TopLevel.GetTopLevel(this) is Window owner)
        {
            controller.SelectFromNative(owner, reset);
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null) { owner.Closed += OnOwnerClosed; }
    }

    private void OnOwnerClosed(object? sender, EventArgs args)
    {
        if (DataContext is SpeechCaptionWindowController controller) { controller.InvalidateNativeChoices(); }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (owner is not null) { owner.Closed -= OnOwnerClosed; owner = null; }
        if (DataContext is SpeechCaptionWindowController controller) { controller.InvalidateNativeChoices(); }
    }
}