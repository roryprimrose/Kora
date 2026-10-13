using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Auditing;

using System.Text.Json.Serialization;

namespace Kora.Core.Diagnostics;

/// <summary>Committed typed store metadata, not executable authority or a diagnostic projection.</summary>
public sealed record AuthorityAuditProvenance(
    int SchemaVersion, string Table, long Sequence, string CommitDigest, string TraceId, string SpanId,
    HostRevision IntentRevision, HostRevision SessionGeneration,
    HostInteractionOutcome? InteractionOutcome, HostId<QuestionIdentity>? QuestionId,
    HostRevision? QuestionRevision, HostId<ApprovalIdentity>? GrantId, HostRevision? GrantRevision,
    IReadOnlyList<AuthorityAuditChange> Changes,
    SecurityAuditEvent? RequestedAudit = null,
    HostRevision? RequestedGrantRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]     IReadOnlyList<AuthorityAuditChange>? RequestedGrants = null)
{
    public Kora.Core.Storage.SessionQueueMoveReceipt? QueueMoveReceipt { get; init; }
    public Kora.Core.Storage.SessionQueueCancelReceipt? QueueCancelReceipt { get; init; }
    public Kora.Core.Storage.SessionQueueRequeueReceipt? QueueRequeueReceipt { get; init; }
}