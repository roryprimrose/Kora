using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionDispositionReceipt(
    HostId<SessionIdentity> SessionId, HostRevision Generation, SessionDispositionPreview Removed);
