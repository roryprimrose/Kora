using Kora.Application.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SpeechPreparationCoordinator? speechPreparation;

    public string SpeechPreparationStatus => speechPreparation?.Status ?? string.Empty;

    private void UpdateSpeechPreparation()
    {
        if (!isInitializing && voiceStartupApplied)
        {
            bool Eligible() => IsCallMutationHostEligible && !communicationPolicy.Current.SuppressSpeech
                && !IsSpeechProviderOperationActive;
            speechPreparation?.Request(Eligible() ? activeSpeechVoice : null, Eligible);
        }
    }

    private void OnSpeechPreparationChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() =>
        {
            if (!disposed) { OnPropertyChanged(nameof(SpeechPreparationStatus)); }
        });
}
