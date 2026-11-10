using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Tools;

/// <summary>One content-free observation, never a complete work inventory or renewed session authority.</summary>
public sealed record SessionMetadataSnapshot(
    string SessionId, string? Name, bool Active, SessionWorkOverviewAvailability Availability,
    DateTimeOffset? ObservedAt, ImmutableArray<SessionMetadataSnapshot.TaskCount>? ObservedTaskCounts,
    int? PendingWork, int? ObservedCurrentFixedReads, int? ObservedPendingQuestions,
    int? OmittedTasks, int? OmittedQueueRecords, int? OmittedQuestions,
    DateTimeOffset? EarliestObservedQuestionExpiry)
{
    public sealed record TaskCount(HostTaskState State, int Count);

    public const string Scope = "One exact content-free session snapshot. Task counts and earliest expiry describe only bounded observed records, not a complete inventory or global earliest deadline. Unknown work remains unavailable.";

    public static SessionMetadataSnapshot From(SessionWorkOverviewRow row)
    {
        var entry = row.Session;
        ValidateEntry(entry);
        if (row.Work is not { } work)
        {
            var unavailable = new SessionMetadataSnapshot(entry.Authority.SessionId.Value.ToString("D"),
                entry.Metadata?.Name.Value, entry.Authority.IsActive, row.Availability,
                null, null, null, null, null, null, null, null, null);
            unavailable.Validate();
            return unavailable;
        }
        work.Validate(entry.Authority.SessionId);
        var snapshot = new SessionMetadataSnapshot(entry.Authority.SessionId.Value.ToString("D"),
            entry.Metadata?.Name.Value, entry.Authority.IsActive, row.Availability, work.ObservedAt,
            [.. Enum.GetValues<HostTaskState>().Select(state => new TaskCount(state,
                work.Tasks.Count(task => task.State == state)))],
            work.PendingCount, work.ObservedCurrentFixedReads, work.PendingQuestions.Length,
            work.OmittedTasks, work.OmittedQueueRecords, work.OmittedQuestions,
            work.PendingQuestions.IsEmpty ? null : work.PendingQuestions.Min(question => question.ExpiresAt));
        snapshot.Validate();
        return snapshot;
    }

    public static bool IsCanonicalId(string? value) => Guid.TryParseExact(value, "D", out var id)
        && id != Guid.Empty && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);

    private static void ValidateEntry(SessionWorkspaceEntry entry)
    {
        entry.Authority.SessionId.Validate();
        if (entry.Authority.Generation.Value < 1
            || entry.Metadata is { } metadata && (metadata.SessionId != entry.Authority.SessionId || metadata.Revision.Value < 1))
        { throw new InvalidDataException("The exact session metadata source is invalid."); }
    }

    public void Validate()
    {
        if (!IsCanonicalId(SessionId) || !Enum.IsDefined(Availability))
        { throw new InvalidDataException("Metadata requires one canonical exact session and explicit availability."); }
        if (Name is not null) { _ = new SessionName(Name); }
        if (Availability != SessionWorkOverviewAvailability.Observed)
        {
            if (ObservedAt is not null || ObservedTaskCounts is not null || PendingWork is not null
                || ObservedCurrentFixedReads is not null || ObservedPendingQuestions is not null
                || OmittedTasks is not null || OmittedQueueRecords is not null || OmittedQuestions is not null
                || EarliestObservedQuestionExpiry is not null)
            { throw new InvalidDataException("Unavailable work cannot be reinterpreted as observed or empty."); }
            return;
        }
        if (ObservedAt is not { Offset: var offset } || offset != TimeSpan.Zero
            || ObservedTaskCounts is not { IsDefault: false } counts
            || counts.Length != Enum.GetValues<HostTaskState>().Length
            || counts.Any(count => count is null || count.Count < 0)
            || !counts.Select(count => count.State).SequenceEqual(Enum.GetValues<HostTaskState>())
            || counts.Sum(count => (long)count.Count) > SessionWorkSnapshot.MaximumRecords
            || PendingWork is not >= 0 || ObservedCurrentFixedReads is not >= 0
            || ObservedPendingQuestions is not (>= 0 and <= SessionWorkSnapshot.MaximumRecords)
            || OmittedTasks is not >= 0 || OmittedQueueRecords is not >= 0 || OmittedQuestions is not >= 0
            || (ObservedPendingQuestions == 0) != (EarliestObservedQuestionExpiry is null)
            || EarliestObservedQuestionExpiry is { Offset: var expiryOffset } && expiryOffset != TimeSpan.Zero)
        { throw new InvalidDataException("Observed metadata requires bounded counts, explicit omissions and UTC observed expiry."); }
    }
}
