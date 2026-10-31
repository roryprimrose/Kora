namespace Kora.Core.Storage;

/// <summary>Exact committed policy and lifecycle readback, not a rollback or forensic-erasure claim.</summary>
public sealed record SessionRetentionApplyReceipt(
    SessionRetentionApplyReview Review, SessionRetentionObservation Retention, SessionRetentionApplyEffect Effect);
