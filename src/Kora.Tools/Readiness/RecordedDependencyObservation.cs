using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Tools.Readiness;

internal static class RecordedDependencyObservation
{
    public static ReadinessObservation Read(string id, IReadOnlyList<DependencyObservation> observations)
    {
        var observation = observations.FirstOrDefault(item => string.Equals(item.Status.Id, id, StringComparison.Ordinal));
        if (observation is null)
        {
            return new(id, null, CapabilityAvailability.NotObserved, "No completed observation is recorded.", null);
        }
        var readiness = observation.Status.Readiness;
        return new(id, readiness, readiness == DependencyReadiness.Ready
                ? CapabilityAvailability.Available : CapabilityAvailability.Unavailable,
            readiness switch
            {
                DependencyReadiness.Ready => "The existing probe completed successfully; this is not a fresh check.",
                DependencyReadiness.Missing => "A required dependency was not found by the existing probe.",
                DependencyReadiness.NeedsConfiguration => "The existing probe requires configuration or a selected prerequisite.",
                DependencyReadiness.Incompatible => "The existing probe found an incompatible dependency.",
                DependencyReadiness.Blocked => "The existing probe reported a blocked dependency.",
                _ => "The existing probe failed; open setup for local recovery details.",
            }, observation.ObservedAt);
    }
}
