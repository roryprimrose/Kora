namespace Kora.Core.Configuration;

/// <summary>Device-local desired startup intent with an explicit unconfirmed-write hold.</summary>
public interface ILogonStartupPreferences
{
    bool? Load();
    bool? ReadBack();
    void BeginWrite();
    void Save(bool? enabled);
    void ConfirmWrite();
}
