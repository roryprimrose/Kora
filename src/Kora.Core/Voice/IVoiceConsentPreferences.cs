namespace Kora.Core.Voice;

public interface IVoiceConsentPreferences
{
    bool? Load();

    void Save(bool consent);
}
