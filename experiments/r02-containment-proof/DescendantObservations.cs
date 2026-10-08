namespace ContainmentProof;

internal static class DescendantObservations
{
    internal static bool ReportedChildrenAreTracked(IEnumerable<int> reported, IReadOnlyCollection<int> tracked) =>
        reported.All(pid => pid > 0 && tracked.Contains(pid));
}
