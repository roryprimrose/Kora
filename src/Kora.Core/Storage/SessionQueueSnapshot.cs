using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionQueueSnapshot(HostId<SessionIdentity> SessionId, HostRevision Generation,
    long Revision, IReadOnlyList<SessionQueueEntry> Entries);
