using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

/// <summary>Content-free conjunction over an already-loaded bounded local-event snapshot.</summary>
public sealed record LocalEventFilter(LocalEventCategory? Category = null, LocalEventReason? Reason = null,
    LocalEventSource? Source = null, LocalEventType? Type = null, RequestOrigin? Origin = null,
    LocalEventQueueReasonFilter QueueReason = default)
{
    public bool IsValid => (Category is null || Enum.IsDefined(Category.Value))
        && (Reason is null || Enum.IsDefined(Reason.Value))
        && (Source is null || Enum.IsDefined(Source.Value))
        && (Type is null || Enum.IsDefined(Type.Value))
        && (Origin is null || Enum.IsDefined(Origin.Value))
        && QueueReason.IsValid;

    public bool Matches(LocalEventView row)
    {
        row.Validate();
        return IsValid
            && (Category is null || row.Event.Category == Category)
            && (Reason is null || row.Reason == Reason)
            && (Source is null || row.Event.Source == Source)
            && (Type is null || row.Event.Type == Type)
            && (Origin is null || row.Event.Origin == Origin)
            && (QueueReason.Match == LocalEventQueueReasonMatch.Any
                || (QueueReason.Match == LocalEventQueueReasonMatch.None
                    ? row.Event.QueueReason is null : row.Event.QueueReason == QueueReason.Value));
    }
}
