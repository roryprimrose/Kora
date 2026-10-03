namespace Kora.Core.Dependencies;

public sealed record DependencyStatus(
    string Id,
    string Name,
    DependencyReadiness Readiness,
    string Detail)
{
    public string DisplayReadiness => Readiness switch
    {
        DependencyReadiness.Ready => "Ready",
        DependencyReadiness.Missing => "Missing",
        DependencyReadiness.NeedsConfiguration => "Needs configuration",
        DependencyReadiness.Incompatible => "Incompatible",
        DependencyReadiness.Blocked => "Blocked",
        DependencyReadiness.Failed => "Failed",
        _ => throw new InvalidOperationException($"Unsupported dependency readiness value: {Readiness}."),
    };
}