using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardReuse(ClipboardSnapshotBroker broker)
{
    public ClipboardOutcome Execute(Guid snapshotId, Func<bool> canPresent) => broker.Reuse(snapshotId, canPresent);
}
