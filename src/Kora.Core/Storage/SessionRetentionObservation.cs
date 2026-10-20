using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>An exact passive retention observation, not mutation or execution authority.</summary>
public sealed record SessionRetentionObservation(
    SessionRetentionState State, HostRevision Generation, long ExemptionAuditSequence,
    bool WorkHoldObserved)
{
    public DateTimeOffset ObservedAt { get; init; }
    public bool Removed { get; init; }

    /// <summary>Compares recorded consequences and the committed exemption identity, not the read clock or transient work holds.</summary>
    public bool MatchesRecordedState(SessionRetentionObservation expected) =>
        State == expected.State && Generation == expected.Generation
        && ExemptionAuditSequence == expected.ExemptionAuditSequence && Removed == expected.Removed;
}
