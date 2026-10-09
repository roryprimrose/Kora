namespace Kora.Core.Configuration;

public interface ISessionRetentionPreferences
{
    SessionRetentionSettings? Load();
    void BeginWrite();
    SessionRetentionSettings? ReadBack();
    void Save(SessionRetentionSettings settings);
    void Reset();
    void ConfirmWrite();
}
