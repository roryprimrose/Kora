using Kora.Core.Configuration;

namespace Kora.Core.Voice;

public interface IWindowsSpeechRateControl
{
    void SetWindowsSpeechRate(WindowsSpeechRate? rate);
}
