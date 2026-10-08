using Kora.Application.Diagnostics;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SpeechPlaybackFrame speechPlaybackFrame = SpeechPlaybackFrame.Inactive;

    public bool IsSpeechPlaybackActive => speechPlaybackFrame.IsPlaying;

    public double SpeechOutputLevel => speechPlaybackFrame.OutputLevel;

    public void RefreshSpeechPlaybackFrame()
    {
        if (disposed || !CanRevealPrivatePresentation)
        {
            RetireSpeechCaption();
            SetSpeechPlaybackFrame(SpeechPlaybackFrame.Inactive);
            return;
        }
        if (!IsSpeaking)
        {
            SetSpeechPlaybackFrame(SpeechPlaybackFrame.Inactive);
            return;
        }

        try
        {
            SetSpeechPlaybackFrame(textToSpeech.PlaybackFrame);
        }
        catch (AudioOutputDeviceUnavailableException exception)
        {
            ApplicationLog.Error(logger, exception, "Reading the speech playback position");
            textToSpeech.InvalidateOutput();
            SetSpeechPlaybackFrame(SpeechPlaybackFrame.Inactive);
            HandleAudioOutputFailure(exception, "Speech playback is unavailable.");
        }
    }

    private void SetSpeechPlaybackFrame(SpeechPlaybackFrame frame)
    {
        SetSpeechCaptionText(speechCaption.Observe(frame, captionResponseId));
        NotifyCaptionState();
        if (speechPlaybackFrame == frame)
        {
            return;
        }

        var wasPlaying = speechPlaybackFrame.IsPlaying;
        var previousLevel = speechPlaybackFrame.OutputLevel;
        speechPlaybackFrame = frame;
        if (wasPlaying != frame.IsPlaying)
        {
            OnPropertyChanged(nameof(IsSpeechPlaybackActive));
        }
        if (previousLevel != frame.OutputLevel)
        {
            OnPropertyChanged(nameof(SpeechOutputLevel));
        }
    }
}
