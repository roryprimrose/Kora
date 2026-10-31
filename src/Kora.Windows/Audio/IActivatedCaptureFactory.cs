using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal interface IActivatedCaptureFactory
{
    // Installed recognition alone does not qualify the inert comparison profile.
    IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => [];

    Task<IActivatedCapture> OpenAsync(
        MicrophoneDevice microphone,
        IReadOnlyList<string> phrases,
        BlockingAudioStream stream,
        Func<bool> canOpen);

    Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone, IReadOnlyList<string> phrases,
        BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow) =>
        OpenAsync(microphone, phrases, stream, canOpen);

    Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone, IReadOnlyList<string> phrases,
        BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow, SpeechInputGeneration profile)
    {
        if (SpeechInputProfileCatalogue.UnavailableReason(profile.Selection) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }
        return OpenAsync(microphone, phrases, stream, canOpen, speechStartWindow);
    }
}
