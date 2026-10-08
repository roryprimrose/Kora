namespace Kora.Core.Configuration;

public interface IAuditRetentionPreferences
{
    AuditRetentionDays? Load();
    AuditRetentionDays? ReadBack();
    void BeginWrite();
    void ConfirmWrite();
    void Save(AuditRetentionDays days);
    void Reset();
}
