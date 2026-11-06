using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal static class NativeComposerSubmitBinding
{
    internal static void Bind(TextBox composer, Button submit)
    {
        composer.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Handled || args.Key != Key.Enter || args.KeyModifiers != KeyModifiers.Control
                || !composer.IsKeyboardFocusWithin || !composer.IsEffectivelyEnabled || !composer.IsEffectivelyVisible
                || TopLevel.GetTopLevel(composer) is not Window { IsVisible: true } window
                || !ReferenceEquals(TopLevel.GetTopLevel(submit), window))
            { return; }

            args.Handled = true;
            if (submit.IsEffectivelyEnabled && submit.IsEffectivelyVisible)
            { submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        }, RoutingStrategies.Tunnel);
    }
}
