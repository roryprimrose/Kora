namespace Kora.Core.Diagnostics;

public sealed class AuditRetentionUnavailableException : InvalidOperationException
{
    private const string Recovery = "Required audit retention is unconfirmed. No new authority mutation or audit commit is permitted; inspect preference and durable receipts before explicit repair.";

    public AuditRetentionUnavailableException() : base(Recovery) { }
    public AuditRetentionUnavailableException(Exception innerException) : base(Recovery, innerException) { }
}
