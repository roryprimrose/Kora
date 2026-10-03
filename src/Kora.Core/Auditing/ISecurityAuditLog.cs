namespace Kora.Core.Auditing;

public interface ISecurityAuditLog
{
    void Write(SecurityAuditEvent auditEvent);
}
