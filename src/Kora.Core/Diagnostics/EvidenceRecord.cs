using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record EvidenceRecord(
    EvidenceReference Reference, DateTimeOffset CommittedUtc, DateTimeOffset DueUtc,
    EvidenceSegmentStatus Retention, HostRequest? Host, TraceSnapshot? Trace,
    Guid? CorrelationId, Guid? ApprovalId, string? Level, string? Category, int? EventId,
    string? Text, IReadOnlyDictionary<string, EvidenceValue> Properties,
    SecurityAuditEvent? Audit, HostOperationOutcome? Outcome,
    IReadOnlyList<EvidenceSegment> RelatedSegments, bool ContentOmitted = false, long? AuditSequence = null);
