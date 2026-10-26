using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

/// <summary>Content-free host observation, never an execution descriptor, question or grant.</summary>
public sealed record LocalEvent(
    Guid Id, long Revision, LocalEventSource Source, LocalEventType Type,
    HostId<SessionIdentity> SessionId, HostId<TaskIdentity>? TaskId, Guid SubjectId,
    long Generation, long SourceRevision, long RelatedRevision, RequestOrigin Origin,
    SessionQueueEligibility? QueueReason, DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionQueueReadProfile? QueueProfile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionQueueCapabilityTarget? QueueTarget { get; init; }
    [JsonIgnore]
    private string ReadName => QueueProfile is { } profile ? "fixed " + SessionQueueReadProfiles.Describe(profile, QueueTarget) : "fixed local-version";
    [JsonIgnore]
    public LocalEventCategory Category => Type switch
    {
        LocalEventType.Failed or LocalEventType.Blocked or LocalEventType.Unknown => LocalEventCategory.Failure,
        LocalEventType.UserAttention => LocalEventCategory.Attention,
        LocalEventType.MaintenanceAvailable => LocalEventCategory.Maintenance,
        _ => LocalEventCategory.Work,
    };
    [JsonIgnore]
    public int Priority => Category switch
    {
        LocalEventCategory.Failure => 3,
        LocalEventCategory.Attention => 2,
        LocalEventCategory.Maintenance => 0,
        _ => 1,
    };
    [JsonIgnore]
    public string Summary => Source is LocalEventSource.ConversationTask or LocalEventSource.ConversationQuestion
        ? Type switch
        {
            LocalEventType.Completed => "An admitted conversation task completed with a durable successful receipt.",
            LocalEventType.Failed => "An admitted conversation task ended without a successful answer; inspect its exact status.",
            LocalEventType.Unknown => "An admitted conversation task has an Unknown outcome; no replay or retry is authorised.",
            _ => "An exact pending conversation clarification needs an answer in the separate Questions surface; this notice cannot answer it.",
        }
        : Type switch
    {
        LocalEventType.Queued => "A " + ReadName + " read is queued; manual dispatch remains separate.",
        LocalEventType.Current => "A " + ReadName + " read is currently admitted.",
        LocalEventType.Completed => "The " + ReadName + " read has a successful durable receipt.",
        LocalEventType.Failed => "The " + ReadName + " read has a failed durable receipt.",
        LocalEventType.Blocked => "The " + ReadName + " queue head is blocked; observed eligibility is not authority.",
        LocalEventType.Unknown => "The " + ReadName + " outcome is Unknown; no replay or retry is authorised.",
        LocalEventType.UserAttention => "An exact pending local-version question needs user attention in its separate Questions window.",
        _ => "Cached verified release availability exists; native maintenance Check/Open remain separate actions.",
    };

    public static Guid Identity(LocalEventSource source, Guid session, Guid subject)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            ((int)source).ToString(CultureInfo.InvariantCulture) + ":" + session.ToString("D") + ":" + subject.ToString("D")));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public bool SameSource(LocalEvent other) => Id == other.Id && Source == other.Source
        && Type == other.Type && SessionId == other.SessionId && TaskId == other.TaskId
        && SubjectId == other.SubjectId && Generation == other.Generation
        && SourceRevision == other.SourceRevision && RelatedRevision == other.RelatedRevision
        && Origin == other.Origin && QueueReason == other.QueueReason && ExpiresAt == other.ExpiresAt
        && QueueProfile == other.QueueProfile && QueueTarget == other.QueueTarget;

    public void Validate()
    {
        if (QueueProfile is { } profile)
        {
            if (Source != LocalEventSource.LocalVersionQueue) { throw new InvalidDataException("Only queue observations carry fixed read metadata."); }
            SessionQueueReadProfiles.Validate(profile, QueueTarget);
        }
        else if (QueueTarget is not null) { throw new InvalidDataException("An event target requires its exact queue profile."); }
        SessionId.Validate();
        TaskId?.Validate();
        if (!Enum.IsDefined(Source) || !Enum.IsDefined(Type) || !Enum.IsDefined(Origin)
            || QueueReason is { } reason && !Enum.IsDefined(reason)
            || SubjectId == Guid.Empty || Id != Identity(Source, SessionId.Value, SubjectId)
            || Revision <= 0 || Generation <= 0 || SourceRevision <= 0 || RelatedRevision < 0
            || ObservedAt.Offset != TimeSpan.Zero || ExpiresAt.Offset != TimeSpan.Zero
            || ExpiresAt <= ObservedAt || ExpiresAt - ObservedAt > TimeSpan.FromHours(6)
            || (Source == LocalEventSource.CachedMaintenance
                ? Type != LocalEventType.MaintenanceAvailable || TaskId is not null || Origin != RequestOrigin.HostSystem || QueueReason is not null
                : TaskId is null || Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    || (Source is LocalEventSource.ConversationTask or LocalEventSource.ConversationQuestion
                        ? QueueReason is not null || (Source == LocalEventSource.ConversationQuestion
                            ? Type != LocalEventType.UserAttention
                            : Type is not (LocalEventType.Completed or LocalEventType.Failed or LocalEventType.Unknown))
                        : Source == LocalEventSource.LocalVersionQuestion
                        ? Type != LocalEventType.UserAttention || QueueReason is not null
                        : Type is LocalEventType.UserAttention or LocalEventType.MaintenanceAvailable || QueueReason is null)))
        {
            throw new InvalidDataException("The host event identity, source, lifetime or revision is invalid.");
        }
    }
}
