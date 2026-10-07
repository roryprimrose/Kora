using Kora.Core.Diagnostics;
using Kora.Core.Tools;

namespace Kora.Application.Tools;

public sealed class CapabilityCaller
{
    internal CapabilityCaller(Guid registryId, HostActivity activity, CapabilityLane lane)
    {
        RegistryId = registryId;
        Activity = activity;
        Lane = lane;
    }

    internal Guid RegistryId { get; }
    internal HostActivity Activity { get; }
    public CapabilityLane Lane { get; }
}
