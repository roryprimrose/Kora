using Kora.Core.Configuration;

namespace Kora.Core.Storage;

/// <summary>A single-target native review; never authority derived from the hypothetical planner.</summary>
public sealed record SessionRetentionApplyReview(
    Guid Id, SessionRetentionObservation Before, SessionRetentionState After,
    SessionRetentionSettings Settings, long PolicyRevision, SessionDispositionPreview Inventory,
    DateTimeOffset Cutoff, string SourceHead, string Snapshot)
{
    public DateTimeOffset ExpiresAt => Cutoff.AddSeconds(30);
    public SessionRetentionApplyEffect Effect =>
        SessionRetentionApplication.Effect(After, Inventory.Session.Authority.IsActive, Cutoff);
}
