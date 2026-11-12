using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
    private WakeSpeechInterruptionCapture? speechInterruption;
    internal bool HasSpeechInterruptionCapture { get { lock (gate) { return speechInterruption is not null; } } }

    internal async Task<WakeSpeechInterruptionCapture?> StartSpeechInterruptionAsync(Func<bool> eligible,
        Func<VoiceTranscriptEventArgs, Func<bool>, Task> dispatch, CancellationToken token)
    {
        var current = observe?.Invoke();
        if (current is null || !eligible()
            || !audio.SupportsSpeechInterruption(current.AssistantName, current.Profile.Selection)) { return null; }
        await PauseWakeForReplyAsync();
        if (observe!() != current || !eligible()) { throw new OperationCanceledException("Original playback admission changed."); }
        bool Admitted()
        {
            var live = observe!();
            var playbackEligible = eligible();
            var supported = audio.SupportsSpeechInterruption(current.AssistantName, current.Profile.Selection);
            lock (gate)
            {
                return !disposed && !profileSwitchActive && !profileRetirementFailed
                    && live == current && playbackEligible && supported;
            }
        }
        var timing = timingSource?.GetTiming() ?? VoiceCaptureTiming.Default;
        timing.Validate();
        WakeSpeechInterruptionCapture capture;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (speechInterruption is not null || comparisonActive || profileSwitchActive || profileRetirementFailed
                || routingContinuation is not null || directRoutingCapture is not null || activatedOpenRequests != 0
                || !captureClosed.Task.IsCompletedSuccessfully || !activated.IsCaptureQuiescent
                || !run.IsCompleted && state != HandsFreeVoiceState.CompletingOperation)
            { throw new InvalidOperationException("Another input owner prevents exact-playback wake capture."); }
            var binding = new HandsFreeAudioBinding(-Interlocked.Increment(ref sequence) - 1,
                current.Microphone, current.AssistantName, current.Profile)
            {
                CommandPhrases = ["stop speaking"],
                Timing = timing,
            };
            capture = speechInterruption = new(audio, binding, Admitted, dispatch, ReleaseSpeechInterruption,
                NotifySpeechInterruptionChanged,
                timeProvider ?? TimeProvider.System, token);
        }
        NotifySpeechInterruptionChanged();
        return capture;
    }

    private void NotifySpeechInterruptionChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void InvalidateSpeechInterruptionCapture()
    {
        WakeSpeechInterruptionCapture? capture;
        lock (gate) { capture = speechInterruption; }
        capture?.Invalidate();
    }

    private void ReleaseSpeechInterruption(WakeSpeechInterruptionCapture capture)
    {
        lock (gate)
        {
            if (ReferenceEquals(speechInterruption, capture)) { speechInterruption = null; }
        }
    }
}
