using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record EvidenceQuery
{
    public EvidenceSource Source { get; init; } = EvidenceSource.All;
    public int Limit { get; init; } = 50;
    public EvidenceReference? Record { get; init; }
    public HostId<SessionIdentity>? SessionId { get; init; }
    public HostId<TaskIdentity>? TaskId { get; init; }
    public HostId<RequestIdentity>? RequestId { get; init; }
    public HostId<InvocationIdentity>? InvocationId { get; init; }
    public Guid? ApprovalId { get; init; }
    public Guid? CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public HostOperationOutcome? Outcome { get; init; }
    public EvidenceContentAvailability ContentAvailability { get; init; }
    public string? EventName { get; init; }
    public string? ExceptionType { get; init; }
    public long? FromAuditSequence { get; init; }
    public long? UntilAuditSequence { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? UntilUtc { get; init; }
    public EvidenceSeverity? Severity { get; init; }
    public int? EventId { get; init; }
    public string? Category { get; init; }
    public string? ActionId { get; init; }
    public SecurityAuditOutcome? AuditOutcome { get; init; }
    public EvidencePropertyFilter? Property { get; init; }
    public string? Text { get; init; }

    public bool MatchesRetainedMetadata(EvidenceRecord record) =>
        (ParentSpanId is null || string.Equals(record.Trace?.ParentSpanId, ParentSpanId, StringComparison.Ordinal))
        && (Outcome is null || record.Outcome == Outcome)
        && (EventName is null || string.Equals(record.EventName, EventName, StringComparison.Ordinal))
        && (ExceptionType is null || string.Equals(record.ExceptionType, ExceptionType, StringComparison.Ordinal))
        && (FromAuditSequence is null || record.AuditSequence >= FromAuditSequence)
        && (UntilAuditSequence is null || record.AuditSequence <= UntilAuditSequence)
        && (ContentAvailability switch
        {
            EvidenceContentAvailability.Any => true,
            EvidenceContentAvailability.Present => !record.ContentOmitted,
            EvidenceContentAvailability.Omitted => record.ContentOmitted,
            _ => throw new InvalidOperationException("The evidence content availability is invalid."),
        });
}
