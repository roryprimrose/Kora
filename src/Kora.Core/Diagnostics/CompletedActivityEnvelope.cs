using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record CompletedActivityEnvelope(
    HostId<EvidenceIdentity> EvidenceId,
    TraceSnapshot Trace,
    HostRequest Host,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    HostOperationOutcome Outcome,
    IReadOnlyList<ActivityLinkEnvelope> Links,
    Guid? AuditCorrelationId = null,
    Guid? ApprovalId = null);
