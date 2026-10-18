using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Reads bounded content-free hypothetical retention impact, without mutation authority.</summary>
public interface ISessionRetentionPlannerReader
{
    /// <summary>Signals that a previously observed source may no longer be current.</summary>
    event EventHandler? Changed;

    /// <summary>Observes the exact proposed policy at one fixed cutoff.</summary>
    ValueTask<SessionRetentionPlanResult> ReadAsync(SessionRetentionSettings proposed, DateTimeOffset cutoff,
        HostRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the exact source and lifetime of an observation.</summary>
    bool IsCurrent(SessionRetentionPlanResult result);
}

/// <summary>Describes whether the bounded observation finished.</summary>
public enum SessionRetentionPlanStatus
{
    /// <summary>Every admitted metadata candidate was validated and aggregated.</summary>
    Complete,
    /// <summary>A finite observation budget was exhausted; no aggregate is published.</summary>
    Incomplete,
    /// <summary>The source or its metadata could not be trusted; no aggregate is published.</summary>
    Unavailable,
}

public sealed record SessionRetentionPlanAggregate(int Sessions, int NewlyArchiveDue, int NewlyDeleteDue,
    int RecordedArchiveDue, int RecordedDeleteDue, int Kept, int WorkHeld, int UnknownHold, int Removed);

public sealed record SessionRetentionPlanResult(SessionRetentionPlanStatus Status,
    SessionRetentionPlanAggregate? Aggregate, string? Snapshot);
