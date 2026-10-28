using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardRead(IClipboardReadWorkflow workflow)
{
    public Task<ClipboardOutcome> ExecuteAsync(Func<bool> canPresent, CancellationToken cancellationToken) =>
        workflow.ReadAsync(canPresent, cancellationToken);
}
