using Kora.Core.Auditing;

namespace Kora.Core.Interaction;

public sealed record HostInteractionCommit(
    HostInteractionSnapshot Snapshot,
    HostInteractionDecision Decision,
    SecurityAuditEvent Audit);
