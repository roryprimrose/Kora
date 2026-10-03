using Kora.Core.Auditing;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Auditing;

internal static partial class SecurityAuditLog
{
    private const string Message =
        "Security audit: {SecurityAudit}; CorrelationId: {CorrelationId}; Category: {AuditCategory}; ActionId: {AuditActionId}; Outcome: {AuditOutcome}; Initiator: {AuditInitiator}; TargetId: {AuditTargetId}; ApprovalId: {ApprovalId}; ReasonCode: {ReasonCode}.";

    [LoggerMessage(150, LogLevel.Information, Message)]
    public static partial void Information(
        ILogger logger,
        bool securityAudit,
        Guid correlationId,
        SecurityAuditCategory auditCategory,
        string auditActionId,
        SecurityAuditOutcome auditOutcome,
        SecurityAuditInitiator auditInitiator,
        string auditTargetId,
        Guid? approvalId,
        string? reasonCode);

    [LoggerMessage(151, LogLevel.Warning, Message)]
    public static partial void Warning(
        ILogger logger,
        bool securityAudit,
        Guid correlationId,
        SecurityAuditCategory auditCategory,
        string auditActionId,
        SecurityAuditOutcome auditOutcome,
        SecurityAuditInitiator auditInitiator,
        string auditTargetId,
        Guid? approvalId,
        string? reasonCode);
}
