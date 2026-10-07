using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private bool rejectingAudioOutputConfiguration;

    public bool CanChangeAudioOutputDevice => false;

    public string AudioOutputConfigurationStatus =>
        "Audio output preference changes are unavailable. The desktop output path is not bound to "
        + "an admitted session/generation and exact host-held endpoint choices. Existing saved routing "
        + "is retained; System still follows the Windows multimedia default. No preference, playback, "
        + "microphone, consent, approval or Windows setting is changed by this refusal.";

    private void RejectAudioOutputConfiguration()
    {
        if (rejectingAudioOutputConfiguration) { return; }
        rejectingAudioOutputConfiguration = true;
        try
        {
            OnPropertyChanged(nameof(SelectedOutputDevice));
            if (!disposed && IsHostInputEligible)
            {
                if (IsResponseInteractionPending)
                {
                    Transcript = AudioOutputConfigurationStatus;
                    return;
                }
                ShowFailure("Audio output preference change unavailable.", AudioOutputConfigurationStatus);
            }
        }
        finally { rejectingAudioOutputConfiguration = false; }
    }

    private bool TryRejectAudioOutputConfiguration(string normalizedTranscript)
    {
        // Reserve this unavailable namespace locally; it must not become a model request,
        // question answer or approval. Text and trace correlation cannot admit a session.
        if (!normalizedTranscript.StartsWith("list audio output settings", StringComparison.OrdinalIgnoreCase)
            && !normalizedTranscript.StartsWith("get audio output device", StringComparison.OrdinalIgnoreCase)
            && !normalizedTranscript.StartsWith("status audio output device", StringComparison.OrdinalIgnoreCase)
            && !normalizedTranscript.StartsWith("set audio output device", StringComparison.OrdinalIgnoreCase)
            && !normalizedTranscript.StartsWith("reset audio output device", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        RejectAudioOutputConfiguration();
        return true;
    }

    private void SetOutputDeviceSnapshot(AudioOutputDevice? value)
    {
        if (SetProperty(ref selectedOutputDevice, value, nameof(SelectedOutputDevice)))
        {
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            NotifyOutputPolicyChanged();
            UpdateOutputDeviceAvailability();
        }
    }
}
