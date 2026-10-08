namespace Kora.Core.Voice;

public interface ISpeechTextPreferences
{
    SpeechTextMode? Load();
    SpeechTextMode? ReadBack();
    void BeginWrite();
    void Save(SpeechTextMode mode);
    void ConfirmWrite();
}
