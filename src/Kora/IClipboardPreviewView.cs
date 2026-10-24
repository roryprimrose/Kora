using Kora.Core.Context;

namespace Kora;

internal interface IClipboardPreviewView
{
    event EventHandler? Closed;
    void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent, Func<Task>? explain = null);
    void SetExplanation(string? text);
    void ClearAndClose();
}
