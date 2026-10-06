using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Auditing;

public sealed class LoggerSecurityAuditLog(
    ILogger<LoggerSecurityAuditLog> logger) : ISecurityAuditLog
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, AuditContext> pending = [];

    public void Write(SecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        lock (sync)
        {
            WriteCorrelated(auditEvent);
        }
    }

    private void WriteCorrelated(SecurityAuditEvent auditEvent)
    {
        var current = HostActivity.Current;
        var context = pending.GetValueOrDefault(auditEvent.CorrelationId)
            ?? new AuditContext(current?.Request ?? HostRequest.Create(RequestOrigin.HostSystem),
                current is null ? null : current.Activity!.Context);
        if (auditEvent.Outcome == SecurityAuditOutcome.Requested && pending.Count >= 1024)
        {
            throw new InvalidOperationException("The bounded pending-audit capacity is exhausted.");
        }
        var warning = auditEvent.Outcome is SecurityAuditOutcome.Failed or SecurityAuditOutcome.Denied;
        using var bootstrap = HostActivity.BeginAudit(context.Request, auditEvent,
            current is null || current.Request != context.Request
                ? context.Cause is { } cause ? [new ActivityLink(cause)] : null
                : null);
        var state = new TrustedAuditState(auditEvent);
        logger.Log(warning ? LogLevel.Warning : LogLevel.Information,
            new EventId(warning ? 151 : 150), state, null,
            static (audit, _) => $"Security audit: {audit.Event.ActionId}; outcome: {audit.Event.Outcome}.");
        bootstrap.Complete(HostOperationOutcome.Completed);
        if (auditEvent.Outcome == SecurityAuditOutcome.Requested)
        {
            pending[auditEvent.CorrelationId] = context with
            {
                Cause = bootstrap.Activity?.Context,
            };
        }
        else
        {
            pending.Remove(auditEvent.CorrelationId);
        }
    }

    private sealed record AuditContext(HostRequest Request, ActivityContext? Cause);
}
