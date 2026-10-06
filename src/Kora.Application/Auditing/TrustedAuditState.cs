using System.Collections;
using Kora.Core.Auditing;

namespace Kora.Application.Auditing;

internal sealed class TrustedAuditState(SecurityAuditEvent auditEvent) : IEnumerable<KeyValuePair<string, object?>>
{
    internal SecurityAuditEvent Event { get; } = auditEvent;

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return new("SecurityAudit", true);
        yield return new("CorrelationId", Event.CorrelationId);
        yield return new("AuditCategory", Event.Category);
        yield return new("AuditActionId", Event.ActionId);
        yield return new("AuditOutcome", Event.Outcome);
        yield return new("AuditInitiator", Event.Initiator);
        yield return new("AuditTargetId", Event.TargetId);
        yield return new("ApprovalId", Event.ApprovalId);
        yield return new("ReasonCode", Event.ReasonCode);
        yield return new("{OriginalFormat}",
            "Security audit: {SecurityAudit}; CorrelationId: {CorrelationId}; Category: {AuditCategory}; ActionId: {AuditActionId}; Outcome: {AuditOutcome}; Initiator: {AuditInitiator}; TargetId: {AuditTargetId}; ApprovalId: {ApprovalId}; ReasonCode: {ReasonCode}.");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
