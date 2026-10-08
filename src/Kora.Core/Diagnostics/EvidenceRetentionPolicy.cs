namespace Kora.Core.Diagnostics;

public sealed class EvidenceRetentionPolicy
{
    public const int DiagnosticDays = Kora.Core.Configuration.DiagnosticRetentionDays.DefaultDays;
    public const int DailyFileDays = 30;
    public const int DailyFileCount = 30;
    public const int DefaultAuditDays = 90;

    public EvidenceRetentionPolicy(int auditDays = DefaultAuditDays)
    {
        if (auditDays is < 30 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(auditDays));
        }
        AuditDays = auditDays;
    }

    public int AuditDays { get; }
    public DateTimeOffset DiagnosticDue(DateTimeOffset committedUtc) => committedUtc.ToUniversalTime().AddDays(DiagnosticDays);
    public DateTimeOffset AuditDue(DateTimeOffset committedUtc) => committedUtc.ToUniversalTime().AddDays(AuditDays);

    public DateTimeOffset ExistingAuditDue(DateTimeOffset committedUtc, DateTimeOffset existingDueUtc, bool applyNow) =>
        applyNow ? AuditDue(committedUtc) : existingDueUtc;
}
