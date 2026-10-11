using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Authorization;

/// <summary>One committed authoritative envelope with only this grant's exact change references.</summary>
public sealed record ExactGrantHistoryEntry(
    HostRequest Request, DateTimeOffset CommittedAt, DateTimeOffset DueAt, EvidenceSegmentStatus Retention,
    SecurityAuditEvent Audit, AuthorityAuditProvenance Provenance, int OtherChanges, int OtherRequestedGrants);
