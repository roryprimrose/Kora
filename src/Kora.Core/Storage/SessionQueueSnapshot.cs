using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionQueueSnapshot(HostId<SessionIdentity> SessionId, HostRevision Generation,
    long Revision, IReadOnlyList<SessionQueueEntry> Entries)
{
    public SessionQueueDispatchControl? DispatchControl { get; init; }
    public IReadOnlyList<SessionQueueOrder> Orders { get; init; } = [];
    public SessionQueueMoveReceipt? MoveReceipt { get; init; }
    public SessionQueueCancelReceipt? CancelReceipt { get; init; }
    public int OmittedOrders { get; init; }
}
