namespace Kora.Core.Dependencies;

public enum DependencyReadiness
{
    Ready,
    Missing,
    NeedsConfiguration,
    Incompatible,
    Blocked,
    Failed,
}