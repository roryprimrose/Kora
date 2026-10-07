using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardRevoke(ClipboardSnapshotBroker broker)
{
    public ClipboardOutcome Execute(Func<bool> canPresent) => broker.Revoke(canPresent);
}
