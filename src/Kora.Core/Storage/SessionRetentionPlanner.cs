using Kora.Core.Configuration;

namespace Kora.Core.Storage;

/// <summary>Authoritative hypothetical aggregation rules; never changes stored clocks or cleanup eligibility.</summary>
public sealed class SessionRetentionPlanner
{
    public const int MaximumCandidates = 4096;
    public const int MaximumEncodedBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromSeconds(30);
    private readonly SessionRetentionSettings proposed;
    private readonly DateTimeOffset cutoff;
    private SessionRetentionPlanAggregate aggregate = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    private int candidates;
    private int bytes;
    private bool incomplete;

    public SessionRetentionPlanner(SessionRetentionSettings proposed, DateTimeOffset cutoff)
    {
        proposed.Validate();
        this.proposed = proposed;
        this.cutoff = cutoff.ToUniversalTime();
    }

    // Call only after validating the complete candidate, including a budget-crossing lookahead.
    public bool AdmitCandidate(int encodedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedBytes);
        if (incomplete || candidates == MaximumCandidates || encodedBytes > MaximumEncodedBytes - bytes)
        {
            incomplete = true;
            return false;
        }
        candidates++;
        bytes += encodedBytes;
        return true;
    }

    public bool Add(SessionRetentionState state, int lifecycle, bool workHeld, bool unknownHold, int encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.SessionId.Validate();
        if (lifecycle is < 0 or > 2 || state.Purged && lifecycle != 2
            || state.LastMeaningfulActivity.Offset != TimeSpan.Zero
            || state.ArchiveDue.Offset != TimeSpan.Zero || state.DeleteDue.Offset != TimeSpan.Zero
            || state.LastMeaningfulActivity > cutoff)
        {
            throw new InvalidDataException("Session retention timing or lifecycle metadata is invalid.");
        }
        DateTimeOffset archive;
        DateTimeOffset delete;
        try
        {
            var archiveTicks = (state.ArchiveDue - state.LastMeaningfulActivity).Ticks;
            var deleteTicks = (state.DeleteDue - state.LastMeaningfulActivity).Ticks;
            if (archiveTicks % TimeSpan.TicksPerDay != 0 || deleteTicks % TimeSpan.TicksPerDay != 0)
            {
                throw new InvalidDataException("Recorded retention deadlines must be whole policy days.");
            }
            _ = new SessionRetentionSettings(checked((int)(archiveTicks / TimeSpan.TicksPerDay)),
                checked((int)(deleteTicks / TimeSpan.TicksPerDay)));
            archive = state.LastMeaningfulActivity.AddDays(proposed.ArchiveDays);
            delete = state.LastMeaningfulActivity.AddDays(proposed.DeleteDays);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("Recorded or hypothetical retention deadlines are invalid.", exception);
        }
        if (!AdmitCandidate(encodedBytes)) { return false; }
        aggregate = aggregate with { Sessions = aggregate.Sessions + 1 };
        if (lifecycle == 2) { aggregate = aggregate with { Removed = aggregate.Removed + 1 }; }
        else if (state.Perpetual) { aggregate = aggregate with { Kept = aggregate.Kept + 1 }; }
        else if (workHeld) { aggregate = aggregate with { WorkHeld = aggregate.WorkHeld + 1 }; }
        else if (unknownHold) { aggregate = aggregate with { UnknownHold = aggregate.UnknownHold + 1 }; }
        else
        {
            aggregate = aggregate with
            {
                NewlyDeleteDue = aggregate.NewlyDeleteDue + (delete <= cutoff && cutoff < state.DeleteDue ? 1 : 0),
                NewlyArchiveDue = aggregate.NewlyArchiveDue
                    + (lifecycle == 0 && archive <= cutoff && cutoff < state.ArchiveDue && delete > cutoff
                        && state.DeleteDue > cutoff ? 1 : 0),
                RecordedArchiveDue = aggregate.RecordedArchiveDue
                    + (lifecycle == 0 && state.ArchiveDue <= cutoff && cutoff < state.DeleteDue ? 1 : 0),
                RecordedDeleteDue = aggregate.RecordedDeleteDue + (state.DeleteDue <= cutoff ? 1 : 0),
            };
        }
        return true;
    }

    public SessionRetentionPlanResult Finish(string snapshot, bool exhausted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        return new(incomplete || exhausted ? SessionRetentionPlanStatus.Incomplete : SessionRetentionPlanStatus.Complete,
            incomplete || exhausted ? null : aggregate, snapshot);
    }

    public static void Validate(SessionRetentionPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status)
            || result.Status == SessionRetentionPlanStatus.Unavailable && result.Snapshot is not null
            || result.Status != SessionRetentionPlanStatus.Unavailable && string.IsNullOrWhiteSpace(result.Snapshot)
            || result.Status != SessionRetentionPlanStatus.Complete && result.Aggregate is not null)
        {
            throw new InvalidDataException("The retention planner source or status is invalid.");
        }
        if (result.Status != SessionRetentionPlanStatus.Complete) { return; }
        if (result.Aggregate is not { } value || value.Sessions is < 0 or > MaximumCandidates
            || value.NewlyArchiveDue < 0 || value.NewlyDeleteDue < 0 || value.RecordedArchiveDue < 0
            || value.RecordedDeleteDue < 0 || value.Kept < 0 || value.WorkHeld < 0 || value.UnknownHold < 0
            || value.Removed < 0
            || (long)value.Kept + value.WorkHeld + value.UnknownHold + value.Removed
                + value.NewlyArchiveDue + value.NewlyDeleteDue > value.Sessions
            || (long)value.Kept + value.WorkHeld + value.UnknownHold + value.Removed
                + value.RecordedArchiveDue + value.RecordedDeleteDue > value.Sessions)
        {
            throw new InvalidDataException("The complete retention planner aggregate is invalid.");
        }
    }
}
