namespace Kora.Core.Interaction;

/// <summary>Run-only original-user choice; never serialized as a preference.</summary>
public sealed record RoutineNoticeQuietState(bool Enabled, long Revision)
{
    public static bool Includes(LocalEventCategory category) =>
        category is LocalEventCategory.Work or LocalEventCategory.Maintenance;
}
