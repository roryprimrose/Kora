using Kora.Core.Tools;

namespace Kora;

internal sealed record QueueCapabilityOption(CapabilityDescriptor Descriptor)
{
    public string Label => Descriptor.Id + " | schema " + Descriptor.SchemaVersion
        + " | " + Descriptor.Input + " -> " + Descriptor.Output + " | " + Descriptor.Availability
        + " | " + Descriptor.Effect + " (registry metadata, not live qualification)";
}
