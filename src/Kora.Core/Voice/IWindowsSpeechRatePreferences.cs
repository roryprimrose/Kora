using Kora.Core.Configuration;

namespace Kora.Core.Voice;

public interface IWindowsSpeechRatePreferences
{
    WindowsSpeechRate? Load();
    void BeginWrite();
    WindowsSpeechRate? ReadBack();
    void ConfirmWrite();
    void Save(WindowsSpeechRate rate);
    void Reset();
}
