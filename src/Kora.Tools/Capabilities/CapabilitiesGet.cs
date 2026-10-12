using Kora.Core.Tools;

namespace Kora.Tools.Capabilities;

public sealed class CapabilitiesGet
{
    internal CapabilityReply Execute(CapabilityIdInput input)
    {
        var target = ReadOnlyCapabilityCatalog.FindExact(input.Id);
        return target is null
            ? new(CapabilityOutcome.Denied, "unknown-capability")
            : new(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: target);
    }
}
