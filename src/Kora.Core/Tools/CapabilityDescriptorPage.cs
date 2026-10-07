namespace Kora.Core.Tools;

public sealed record CapabilityDescriptorPage(
    IReadOnlyList<CapabilityDescriptor> Records, int TotalRecords, int? NextOffset);
