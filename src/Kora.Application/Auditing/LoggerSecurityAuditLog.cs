using Kora.Core.Auditing;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Auditing;

public sealed class LoggerSecurityAuditLog(
    ILogger<LoggerSecurityAuditLog> logger) : ISecurityAuditLog
{
    public void Write(SecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        if (auditEvent.Outcome is SecurityAuditOutcome.Failed or SecurityAuditOutcome.Denied)
        {
            SecurityAuditLog.Warning(
                logger,
                true,
                auditEvent.CorrelationId,
                auditEvent.Category,
                auditEvent.ActionId,
                auditEvent.Outcome,
                auditEvent.Initiator,
                auditEvent.TargetId,
                auditEvent.ApprovalId,
                auditEvent.ReasonCode);
            return;
        }

        SecurityAuditLog.Information(
            logger,
            true,
            auditEvent.CorrelationId,
            auditEvent.Category,
            auditEvent.ActionId,
            auditEvent.Outcome,
            auditEvent.Initiator,
            auditEvent.TargetId,
            auditEvent.ApprovalId,
            auditEvent.ReasonCode);
    }
}
