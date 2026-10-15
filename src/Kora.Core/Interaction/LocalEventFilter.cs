using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

/// <summary>Content-free conjunction over an already-loaded bounded local-event snapshot.</summary>
public sealed record LocalEventFilter(LocalEventCategory? Category = null, LocalEventReason? Reason = null,
    LocalEventSource? Source = null, LocalEventType? Type = null, RequestOrigin? Origin = null,
    LocalEventQueueReasonFilter QueueReason = default,
    string SessionId = "", string TaskId = "", string SubjectId = "")
{
    public bool IsValid => (Category is null || Enum.IsDefined(Category.Value))
        && (Reason is null || Enum.IsDefined(Reason.Value))
        && (Source is null || Enum.IsDefined(Source.Value))
        && (Type is null || Enum.IsDefined(Type.Value))
        && (Origin is null || Enum.IsDefined(Origin.Value))
        && QueueReason.IsValid
        && IsIdentity(SessionId) && IsIdentity(TaskId) && IsIdentity(SubjectId);

    private static bool IsIdentity(string? value) => value is not null
        && (value.Length == 0 || SessionCommand.ExactId(value, out var id)
            && string.Equals(value, id.ToString("D"), StringComparison.Ordinal));

    public bool Matches(LocalEventView row)
    {
        row.Validate();
        return IsValid
            && (Category is null || row.Event.Category == Category)
            && (Reason is null || row.Reason == Reason)
            && (Source is null || row.Event.Source == Source)
            && (Type is null || row.Event.Type == Type)
            && (Origin is null || row.Event.Origin == Origin)
            && (SessionId.Length == 0 || string.Equals(SessionId, row.Event.SessionId.Value.ToString("D"), StringComparison.Ordinal))
            && (TaskId.Length == 0 || string.Equals(TaskId, row.Event.TaskId?.Value.ToString("D"), StringComparison.Ordinal))
            && (SubjectId.Length == 0 || string.Equals(SubjectId, row.Event.SubjectId.ToString("D"), StringComparison.Ordinal))
            && (QueueReason.Match == LocalEventQueueReasonMatch.Any
                || (QueueReason.Match == LocalEventQueueReasonMatch.None
                    ? row.Event.QueueReason is null : row.Event.QueueReason == QueueReason.Value));
    }
}
