using Kora.Core.Dependencies;

namespace Kora.Core.Tools;

public sealed record ReadinessObservation(
    string Id, DependencyReadiness? Readiness, CapabilityAvailability Availability,
    string Reason, DateTimeOffset? ObservedAt);
