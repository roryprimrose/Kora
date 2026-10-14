namespace Kora.Core.Interaction;

/// <summary>Content-free conjunction over an already-loaded bounded local-event snapshot.</summary>
public sealed record LocalEventFilter(LocalEventCategory? Category = null, LocalEventReason? Reason = null)
{
    public bool IsValid => (Category is null || Enum.IsDefined(Category.Value))
        && (Reason is null || Enum.IsDefined(Reason.Value));

    public bool Matches(LocalEventView row) => IsValid
        && Enum.IsDefined(row.Event.Type) && Enum.IsDefined(row.Reason)
        && (Category is null || row.Event.Category == Category)
        && (Reason is null || row.Reason == Reason);
}
