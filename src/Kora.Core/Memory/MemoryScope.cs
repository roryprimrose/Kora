using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryScope
{
    private MemoryScope(MemoryScopeKind kind, Guid identity)
    {
        Kind = kind;
        Identity = identity;
    }

    public MemoryScopeKind Kind { get; }
    public Guid Identity { get; }

    public static MemoryScope Session(HostId<SessionIdentity> identity)
    {
        identity.Validate();
        return new(MemoryScopeKind.Session, identity.Value);
    }

    public static MemoryScope DeviceProfile(HostId<DeviceProfileIdentity> identity)
    {
        identity.Validate();
        return new(MemoryScopeKind.DeviceProfile, identity.Value);
    }

    public static MemoryScope Project(HostId<MemoryProjectIdentity> identity)
    {
        identity.Validate();
        return new(MemoryScopeKind.Project, identity.Value);
    }

    public static MemoryScope Source(HostId<MemorySourceIdentity> identity)
    {
        identity.Validate();
        return new(MemoryScopeKind.Source, identity.Value);
    }
}
