using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora.NativeUxFixture;

internal sealed class NativeUxClipboardGuard : IDisposable
{
    private readonly IDisposable copying = TextBox.CopyingToClipboardEvent.AddClassHandler<TextBox>(
        static (_, args) => args.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    private readonly IDisposable cutting = TextBox.CuttingToClipboardEvent.AddClassHandler<TextBox>(
        static (_, args) => args.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    private readonly IDisposable pasting = TextBox.PastingFromClipboardEvent.AddClassHandler<TextBox>(
        static (_, args) => args.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    private readonly IDisposable contexts = Control.ContextRequestedEvent.AddClassHandler<Control>(
        static (_, args) => args.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    private readonly IDisposable keys = InputElement.KeyDownEvent.AddClassHandler<Window>(static (_, args) =>
    {
        if (((args.KeyModifiers & KeyModifiers.Control) != KeyModifiers.None && args.Key is Key.C or Key.V or Key.X or Key.Insert)
            || ((args.KeyModifiers & KeyModifiers.Shift) != KeyModifiers.None && args.Key is Key.Insert or Key.Delete))
        {
            args.Handled = true;
        }
    }, RoutingStrategies.Tunnel);

    public void Dispose()
    {
        keys.Dispose();
        contexts.Dispose();
        pasting.Dispose();
        cutting.Dispose();
        copying.Dispose();
    }
}
