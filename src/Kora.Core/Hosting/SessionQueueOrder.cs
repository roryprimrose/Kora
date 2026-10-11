using System.Collections.Immutable;

namespace Kora.Core.Hosting;

/// <summary>Audited pending-order lineage, separate from immutable arrival and active admission identity.</summary>
public sealed record SessionQueueOrder(int Version, Guid Id, HostId<SessionIdentity> SessionId,
    HostRevision Generation, Guid RunId, long Revision, ImmutableArray<HostId<TaskIdentity>> Tasks)
{
    public void Validate(IReadOnlyList<SessionQueueEntry> entries)
    {
        SessionId.Validate();
        if (Version != 1 || Id == Guid.Empty || Generation.Value <= 0 || RunId == Guid.Empty || Revision <= 0
            || Tasks.IsDefault || Tasks.Length > SessionQueueLimits.MaximumPendingPerSession
            || Tasks.Any(task => task.Value == Guid.Empty) || Tasks.Distinct().Count() != Tasks.Length)
        {
            throw new InvalidDataException("The explicit queue order format, lineage or unique pending identities are invalid.");
        }
        var pending = entries.Where(entry => entry.Request.SessionId == SessionId && entry.RunId == RunId
            && entry.Generation == Generation && entry.IsPending).ToArray();
        if (pending.Length != Tasks.Length || pending.Any(entry => !Tasks.Contains(entry.Request.TaskId)))
        {
            throw new InvalidDataException("Explicit queue order does not name exactly its same-session, same-run pending set.");
        }
        if (pending.Any(entry => entry.Dependency is { } dependency && Tasks.Contains(dependency)
            && Tasks.IndexOf(dependency) >= Tasks.IndexOf(entry.Request.TaskId)))
        {
            throw new InvalidDataException("Explicit queue order inverts a pending prerequisite.");
        }
    }
}
