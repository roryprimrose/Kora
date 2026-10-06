using Avalonia.Input.Platform;

namespace Kora;

internal sealed class NativeDetailClipboard : IDetailClipboard
{
    public async Task WritePlainTextAsync(IDetailView view, string source)
    {
        var clipboard = (view as DetailWindow)?.Clipboard
            ?? throw new InvalidOperationException("The native clipboard is unavailable.");
        // Exactly one Unicode text format. No HTML, clipboard reads, exports, or context capture.
        await clipboard.SetTextAsync(source);
    }
}
