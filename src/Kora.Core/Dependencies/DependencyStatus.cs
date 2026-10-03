namespace Kora.Core.Dependencies;

public sealed record DependencyStatus(
    string Id,
    string Name,
    DependencyReadiness Readiness,
    string Detail);