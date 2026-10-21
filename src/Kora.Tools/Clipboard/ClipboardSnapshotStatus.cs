using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardSnapshotStatus(ClipboardSnapshotBroker broker)
{
    public void Execute(Guid snapshotId, Func<bool> canPresent,
        Action<ClipboardSnapshotStatusResult> publish, CancellationToken cancellationToken) =>
        broker.PublishStatus(snapshotId, canPresent, publish, cancellationToken);
}
