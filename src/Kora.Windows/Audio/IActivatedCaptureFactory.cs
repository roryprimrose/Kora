using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal interface IActivatedCaptureFactory
{
    bool IsAvailable => true;
    bool IsQuiescent => true;
    string? GetUnavailableReason(SpeechInputSelection selection) => SpeechInputProfileCatalogue.UnavailableReason(selection);
    Task RetireAsync() => Task.CompletedTask;

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
        BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow, TimeSpan trailingSilence, SpeechInputGeneration profile)
    {
        if (trailingSilence != VoiceCaptureTiming.Default.TrailingSilence)
        { throw new InvalidOperationException("This capture factory does not support requested trailing-silence timing."); }
        return OpenAsync(microphone, phrases, stream, canOpen, speechStartWindow, profile);
    }

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
