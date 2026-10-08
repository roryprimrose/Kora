namespace Kora.Core.Diagnostics;

public sealed class EvidenceRetentionPolicy
{
    public const int DiagnosticDays = Kora.Core.Configuration.DiagnosticRetentionDays.DefaultDays;
    public const int DailyFileDays = 30;
    public const int DailyFileCount = 30;
    public const int DefaultAuditDays = Kora.Core.Configuration.AuditRetentionDays.DefaultDays;

    public EvidenceRetentionPolicy(int auditDays = DefaultAuditDays)
    {
        AuditDays = new Kora.Core.Configuration.AuditRetentionDays(auditDays).Days;
    }

    public int AuditDays { get; }
    public DateTimeOffset DiagnosticDue(DateTimeOffset committedUtc) => committedUtc.ToUniversalTime().AddDays(DiagnosticDays);
    public DateTimeOffset AuditDue(DateTimeOffset committedUtc) => committedUtc.ToUniversalTime().AddDays(AuditDays);

    public DateTimeOffset ExistingAuditDue(DateTimeOffset committedUtc, DateTimeOffset existingDueUtc, bool applyNow) =>
        applyNow ? AuditDue(committedUtc) : existingDueUtc;
}
