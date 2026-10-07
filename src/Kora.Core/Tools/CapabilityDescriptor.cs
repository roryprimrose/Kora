namespace Kora.Core.Tools;

public sealed record CapabilityDescriptor(
    string Id,
    int SchemaVersion,
    CapabilityInputShape Input,
    CapabilityOutputShape Output,
    CapabilityEffect Effect,
    CapabilityAvailability Availability,
    IReadOnlyList<CapabilityLane> CallerLanes,
    CapabilityLimits Limits);
