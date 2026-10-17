using Kora.Core.Auditing;

namespace Kora.Application.Auditing;

public static class SecurityAuditLogExtensions
{
    /// <summary>Awaits durable audit delivery without performing synchronous sink I/O on the caller's thread.</summary>
    public static Task WriteAsync(this ISecurityAuditLog auditLog, SecurityAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(auditEvent);
        return Task.Run(() => auditLog.Write(auditEvent), cancellationToken);
    }
}
