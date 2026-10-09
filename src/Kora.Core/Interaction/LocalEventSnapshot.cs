namespace Kora.Core.Interaction;

public sealed record LocalEventSnapshot(IReadOnlyList<LocalEventView> Events, int Omitted, LocalEventReason Reason)
{
    public const int MaximumVisible = 8;
    public const string Scope = "Visual native local observations only. No focus, question replacement, voice target, activity extension, grant, work dispatch, model, network or maintenance action.";
}
