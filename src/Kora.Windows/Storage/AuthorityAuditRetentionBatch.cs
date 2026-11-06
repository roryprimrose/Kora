namespace Kora.Windows.Storage;

public sealed record AuthorityAuditRetentionBatch(int Removed, long PrunedThrough, DateTimeOffset? Cutoff,
    bool HasDueBacklog, bool ReferenceHeld);
