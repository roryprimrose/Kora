namespace Kora.Core.Dependencies;

public sealed record DependencyObservation(DependencyStatus Status, DateTimeOffset ObservedAt);
