namespace Kora.Core.Interaction;

/// <summary>Content-free conjunction over an already-loaded bounded local-event snapshot.</summary>
public sealed record LocalEventFilter(LocalEventCategory? Category = null, LocalEventReason? Reason = null,
    LocalEventSource? Source = null, LocalEventType? Type = null)
{
    public bool IsValid => (Category is null || Enum.IsDefined(Category.Value))
        && (Reason is null || Enum.IsDefined(Reason.Value))
        && (Source is null || Enum.IsDefined(Source.Value))
        && (Type is null || Enum.IsDefined(Type.Value));

    public bool Matches(LocalEventView row) => IsValid
        && Enum.IsDefined(row.Event.Source) && Enum.IsDefined(row.Event.Type) && Enum.IsDefined(row.Reason)
        && (Category is null || row.Event.Category == Category)
        && (Reason is null || row.Reason == Reason)
        && (Source is null || row.Event.Source == Source)
        && (Type is null || row.Event.Type == Type);
}
