using Kora.Core.Dependencies;

namespace Kora.Core.Tools;

public sealed record RuntimeObservation(
    string Id, RuntimeLocality Locality, CapabilityAvailability Availability,
    DependencyReadiness? Readiness, string Reason, DateTimeOffset? ObservedAt,
    bool ToolLoopQualified);
