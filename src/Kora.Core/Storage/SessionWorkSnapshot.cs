using System.Collections.Immutable;

using Kora.Core.Hosting;
namespace Kora.Core.Storage;

public sealed record SessionWorkSnapshot(
    SessionWorkspaceEntry Session, long AuthorityRevision, DateTimeOffset ObservedAt,
    SessionQueueSnapshot Queue, int PendingCount, int PendingCapacity, int ExecutionSlots,
    ImmutableArray<SessionQueueObservation> QueueRecords, int OmittedQueueRecords,
    ImmutableArray<HostTaskObservation> Tasks, int OmittedTasks,
    ImmutableArray<SessionWorkQuestion> PendingQuestions, int OmittedQuestions)
{
    public const int MaximumRecords = 50;
    public const int RecentQueueRecords = 25;
    public const string Scope = "Bounded authoritative work observation, not a conversation or executor. "
        + "Deadlines and eligibility are as observed; omitted records are explicit gaps. "
        + "Manual dispatch is fair across eligible sessions. Restart never replays work; Unknown quarantines. "
        + "Questions remain exactly bound in their separate native window; selection never retargets them. "
        + "History, evidence, immutable details and volatile previews/captions are separate sources.";

    public void RequireSubject(HostId<SessionIdentity> addressed)
    {
        addressed.Validate();
        if (Session.Authority.SessionId != addressed || Queue.SessionId != addressed
            || Queue.Generation != Session.Authority.Generation)
        {
            throw new InvalidDataException("The work snapshot does not belong to the exact addressed session generation.");
        }
    }
}
