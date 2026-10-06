namespace Kora.Core.Auditing;

public enum SecurityAuditOutcome
{
    Requested,
    Succeeded,
    Failed,
    Denied,
    Cancelled,
    Unknown,
}
