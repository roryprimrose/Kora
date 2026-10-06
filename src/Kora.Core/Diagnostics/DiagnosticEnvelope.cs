using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record DiagnosticEnvelope(
    int SchemaVersion,
    HostId<EvidenceIdentity> EvidenceId,
    DateTimeOffset ObservedUtc,
    int EventId,
    string? EventName,
    string Level,
    string Category,
    string MessageTemplate,
    IReadOnlyDictionary<string, EvidenceValue> Properties,
    IReadOnlyList<IReadOnlyDictionary<string, EvidenceValue>> Scopes,
    TraceSnapshot? Trace,
    HostRequest? Host,
    string? ExceptionType,
    Guid? AuditCorrelationId = null,
    Guid? ApprovalId = null);
