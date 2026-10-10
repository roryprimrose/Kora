using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryBoundary(
    HostId<DeviceProfileIdentity> Profile, HostId<SessionIdentity> Session, HostRevision Generation,
    HostId<MemoryProjectIdentity>? Project, HostId<MemorySourceIdentity>? Source,
    HostRevision? SourceRevision, long Revision, bool IsPrivate, bool IsOwner, bool LineageKnown);
