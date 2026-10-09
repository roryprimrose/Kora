namespace Kora.Core.Diagnostics;

public sealed record EvidenceSnapshot(long Log, long Audit, long Span, long Link,
    string? LogCeilingId = null, string? SpanCeilingId = null, string? LinkCeilingId = null,
    string? DailySnapshotId = null, long? AuthorityCeiling = null,
    string? AuthorityCeilingDigest = null, string? AuthorityStoreIdentity = null);
