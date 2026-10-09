namespace Kora.Core.Voice;

public interface ISpeechTextPreferences
{
    SpeechTextMode? Load();
    SpeechTextMode? ReadBack();
    SpeechCaptionOptions? LoadOptions();
    SpeechCaptionOptions? ReadBackOptions();
    void BeginWrite();
    void Save(SpeechTextMode mode);
    void SaveOptions(SpeechCaptionOptions options);
    void ConfirmWrite();
}
