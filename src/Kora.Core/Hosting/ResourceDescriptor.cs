namespace Kora.Core.Hosting;

public sealed record ResourceDescriptor
{
    public ResourceDescriptor(HostId<ResourceIdentity> identity, ResourceAccess access)
    {
        identity.Validate();
        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
        Identity = identity;
        Access = access;
    }

    public HostId<ResourceIdentity> Identity { get; }
    public ResourceAccess Access { get; }
    public bool AllowsConcurrency => Access == ResourceAccess.SharedRead;
}
