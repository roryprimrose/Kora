using System.Runtime.InteropServices;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Interaction;

/// <summary>Content-free conjunction over an already-loaded bounded local-event snapshot.</summary>
public sealed record LocalEventFilter(LocalEventCategory? Category = null, LocalEventReason? Reason = null,
    LocalEventSource? Source = null, LocalEventType? Type = null, RequestOrigin? Origin = null,
    LocalEventQueueReasonFilter QueueReason = default,
    string SessionId = "", string TaskId = "", string SubjectId = "",
    string MinimumRevision = "", string MaximumRevision = "",
    string MinimumGeneration = "", string MaximumGeneration = "",
    string ObservedFromUtc = "", string ObservedUntilUtc = "",
    string ExpiresFromUtc = "", string ExpiresUntilUtc = "")
{
    public bool IsValid => MetadataValid && TryGetBounds(out _);

    private bool MetadataValid => (Category is null || Enum.IsDefined(Category.Value))
        && (Reason is null || Enum.IsDefined(Reason.Value))
        && (Source is null || Enum.IsDefined(Source.Value))
        && (Type is null || Enum.IsDefined(Type.Value))
        && (Origin is null || Enum.IsDefined(Origin.Value))
        && QueueReason.IsValid
        && IsIdentity(SessionId) && IsIdentity(TaskId) && IsIdentity(SubjectId);

    private static bool IsIdentity(string? value) => value is not null
        && (value.Length == 0 || SessionCommand.ExactId(value, out var id)
            && string.Equals(value, id.ToString("D"), StringComparison.Ordinal));

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Bounds(long? MinimumRevision, long? MaximumRevision,
        long? MinimumGeneration, long? MaximumGeneration,
        DateTimeOffset? ObservedFrom, DateTimeOffset? ObservedUntil,
        DateTimeOffset? ExpiresFrom, DateTimeOffset? ExpiresUntil);

    private bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        try
        {
            bounds = new(OptionalUnsignedDecimal.Parse(MinimumRevision), OptionalUnsignedDecimal.Parse(MaximumRevision),
                OptionalUnsignedDecimal.Parse(MinimumGeneration), OptionalUnsignedDecimal.Parse(MaximumGeneration),
                OptionalCanonicalUtc.Parse(ObservedFromUtc), OptionalCanonicalUtc.Parse(ObservedUntilUtc),
                OptionalCanonicalUtc.Parse(ExpiresFromUtc), OptionalCanonicalUtc.Parse(ExpiresUntilUtc));
            return bounds.MinimumRevision is not <= 0 && bounds.MaximumRevision is not <= 0
                && bounds.MinimumGeneration is not <= 0 && bounds.MaximumGeneration is not <= 0
                && !(bounds.MinimumRevision > bounds.MaximumRevision)
                && !(bounds.MinimumGeneration > bounds.MaximumGeneration)
                && !(bounds.ObservedFrom > bounds.ObservedUntil)
                && !(bounds.ExpiresFrom > bounds.ExpiresUntil);
        }
        catch (InvalidDataException) { return false; }
        catch (ArgumentNullException) { return false; }
    }

    public bool Matches(LocalEventView row)
    {
        row.Validate();
        return MetadataValid && TryGetBounds(out var bounds)
            && (Category is null || row.Event.Category == Category)
            && (Reason is null || row.Reason == Reason)
            && (Source is null || row.Event.Source == Source)
            && (Type is null || row.Event.Type == Type)
            && (Origin is null || row.Event.Origin == Origin)
            && (SessionId.Length == 0 || string.Equals(SessionId, row.Event.SessionId.Value.ToString("D"), StringComparison.Ordinal))
            && (TaskId.Length == 0 || string.Equals(TaskId, row.Event.TaskId?.Value.ToString("D"), StringComparison.Ordinal))
            && (SubjectId.Length == 0 || string.Equals(SubjectId, row.Event.SubjectId.ToString("D"), StringComparison.Ordinal))
            && (bounds.MinimumRevision is not { } minimumRevision || row.Event.Revision >= minimumRevision)
            && (bounds.MaximumRevision is not { } maximumRevision || row.Event.Revision <= maximumRevision)
            && (bounds.MinimumGeneration is not { } minimumGeneration || row.Event.Generation >= minimumGeneration)
            && (bounds.MaximumGeneration is not { } maximumGeneration || row.Event.Generation <= maximumGeneration)
            && (bounds.ObservedFrom is not { } observedFrom || row.Event.ObservedAt >= observedFrom)
            && (bounds.ObservedUntil is not { } observedUntil || row.Event.ObservedAt <= observedUntil)
            && (bounds.ExpiresFrom is not { } expiresFrom || row.Event.ExpiresAt >= expiresFrom)
            && (bounds.ExpiresUntil is not { } expiresUntil || row.Event.ExpiresAt <= expiresUntil)
            && (QueueReason.Match == LocalEventQueueReasonMatch.Any
                || (QueueReason.Match == LocalEventQueueReasonMatch.None
                    ? row.Event.QueueReason is null : row.Event.QueueReason == QueueReason.Value));
    }
}
