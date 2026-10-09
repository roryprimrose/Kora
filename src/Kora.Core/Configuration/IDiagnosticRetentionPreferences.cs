namespace Kora.Core.Configuration;

public interface IDiagnosticRetentionPreferences
{
    DiagnosticRetentionDays? Load();
    DiagnosticRetentionDays? ReadBack();
    void BeginWrite();
    void ConfirmWrite();
    void Save(DiagnosticRetentionDays days);
    void Reset();
}
