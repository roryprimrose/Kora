namespace Kora.Core.Interaction;

public sealed record LocalEventSnapshot(IReadOnlyList<LocalEventView> Events, int Omitted, LocalEventReason Reason)
{
    public RoutineNoticeQuietState RoutineQuiet { get; init; } = new(false, 0);
    public int RoutineOmitted { get; init; }
    public int RoutineSuppressed { get; init; }
    public long AdmissionRevision { get; init; }
    public long NoticeRevision { get; init; }
    public const int MaximumVisible = 8;
    public const string Scope = "Visual native local observations only. No focus, question replacement, voice target, activity extension, grant, work dispatch, model, network or maintenance action.";
}
