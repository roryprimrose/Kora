using Kora.Core.Context;

namespace Kora;

internal interface IClipboardPreviewView
{
    event EventHandler? Closed;
    void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent, Func<Task>? explain = null,
        Func<CancellationToken, ClipboardJsonSyntaxResult>? validateJson = null, Func<bool>? isJsonCurrent = null,
        Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardXmlValidationResult>? validateXml = null);
    void ClearJsonValidation();
    void SetExplanation(string? text);
    void SetBase64Inspection(Func<ClipboardSnapshot, CancellationToken, ClipboardBase64Inspection> inspect);
    void ClearAndClose();
}
