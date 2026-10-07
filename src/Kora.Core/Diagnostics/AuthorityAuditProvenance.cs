using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Diagnostics;

/// <summary>Committed typed store metadata, not executable authority or a diagnostic projection.</summary>
public sealed record AuthorityAuditProvenance(
    int SchemaVersion, string Table, long Sequence, string CommitDigest, string TraceId, string SpanId,
    HostRevision IntentRevision, HostRevision SessionGeneration,
    HostInteractionOutcome? InteractionOutcome, HostId<QuestionIdentity>? QuestionId,
    HostRevision? QuestionRevision, HostId<ApprovalIdentity>? GrantId, HostRevision? GrantRevision,
    IReadOnlyList<AuthorityAuditChange> Changes);
