using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>An exact passive retention observation, not mutation or execution authority.</summary>
public sealed record SessionRetentionObservation(
    SessionRetentionState State, HostRevision Generation, long ExemptionAuditSequence,
    bool WorkHoldObserved)
{
    public DateTimeOffset ObservedAt { get; init; }
    public bool Removed { get; init; }
}
