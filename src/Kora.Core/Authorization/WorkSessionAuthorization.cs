using Kora.Core.Hosting;

namespace Kora.Core.Authorization;

public sealed record WorkSessionAuthorization(
    HostId<SessionIdentity> SessionId,
    HostRevision Generation,
    bool IsActive);
