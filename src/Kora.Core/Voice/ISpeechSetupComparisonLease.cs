namespace Kora.Core.Voice;

/// <summary>One exclusive lifetime; disposal cancels and joins all capture and raw-buffer cleanup.</summary>
public interface ISpeechSetupComparisonLease : IAsyncDisposable
{
    /// <summary>Captures one finite inert utterance, returning only after native resources have joined.</summary>
    Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
        CancellationToken cancellationToken);

    /// <summary>Captures without a presentation observer; never publishes to ordinary voice events.</summary>
    Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, CancellationToken cancellationToken) =>
        CaptureAsync(profile, static () => { }, cancellationToken);
}
