using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardRead(ClipboardSnapshotBroker broker)
{
    public Task<ClipboardOutcome> ExecuteAsync(Func<bool> canPresent, CancellationToken cancellationToken) =>
        broker.CaptureAsync(canPresent, cancellationToken);
}
