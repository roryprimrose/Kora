namespace Kora.Core.Platform;

public sealed class CapturePrivacyReceiptEventArgs(
    PrivacyObservation observation,
    long generation,
    WindowsSessionState sessionState,
    bool wasRecording,
    bool hadRecorder,
    bool hadPendingOpen,
    long buffersClearedTimestamp,
    int bufferedBytesAfterClear,
    long? recorderReleasedTimestamp,
    bool releaseConfirmed) : EventArgs
{
    public PrivacyObservation Observation { get; } = observation;
    public long Generation { get; } = generation;
    public WindowsSessionState SessionState { get; } = sessionState;
    public bool WasRecording { get; } = wasRecording;
    public bool HadRecorder { get; } = hadRecorder;
    public bool HadPendingOpen { get; } = hadPendingOpen;
    public long BuffersClearedTimestamp { get; } = buffersClearedTimestamp;
    public int BufferedBytesAfterClear { get; } = bufferedBytesAfterClear;
    public long? RecorderReleasedTimestamp { get; } = recorderReleasedTimestamp;
    public bool ReleaseConfirmed { get; } = releaseConfirmed;

    public double? ObservedToReleaseMilliseconds => RecorderReleasedTimestamp is { } released
        ? (double)((decimal)released - Observation.ObservedTimestamp) * 1000 / Observation.TimestampFrequency
        : null;

    public bool? LockReleaseWithinTarget => SessionState != WindowsSessionState.Locked || !HadRecorder
        || RecorderReleasedTimestamp is null
        || !WasRecording && RecorderReleasedTimestamp < Observation.ObservedTimestamp
        ? null
        : ReleaseConfirmed && RecorderReleasedTimestamp >= Observation.ObservedTimestamp
            && ((decimal)RecorderReleasedTimestamp.Value - Observation.ObservedTimestamp) * 1000
                <= (decimal)Observation.TimestampFrequency * 500;
}
