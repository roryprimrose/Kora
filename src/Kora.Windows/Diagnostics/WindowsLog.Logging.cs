using Microsoft.Extensions.Logging;
using Kora.Core.Platform;

namespace Kora.Windows.Diagnostics;

internal static partial class WindowsLog
{
    [LoggerMessage(200, LogLevel.Debug, "{Operation}.")]
    public static partial void Debug(ILogger logger, string operation);

    [LoggerMessage(201, LogLevel.Information, "{Operation}.")]
    public static partial void Information(ILogger logger, string operation);

    [LoggerMessage(202, LogLevel.Warning, "{Operation}.")]
    public static partial void Warning(ILogger logger, string operation);

    [LoggerMessage(203, LogLevel.Error, "{Operation} failed.")]
    public static partial void Error(ILogger logger, Exception exception, string operation);

    [LoggerMessage(204, LogLevel.Debug, "Enumerated {DeviceCount} {DeviceType} devices.")]
    public static partial void DevicesEnumerated(ILogger logger, int deviceCount, string deviceType);

    [LoggerMessage(
        205,
        LogLevel.Information,
        "Voice activation started with {PhraseCount} grammar phrases.")]
    public static partial void VoiceActivationStarted(ILogger logger, int phraseCount);

    [LoggerMessage(
        206,
        LogLevel.Debug,
        "Accepted a recognized command with confidence {Confidence}.")]
    public static partial void CommandRecognized(ILogger logger, float confidence);

    [LoggerMessage(207, LogLevel.Information, "Speech synthesis and playback started.")]
    public static partial void SpeechStarted(ILogger logger);

    [LoggerMessage(208, LogLevel.Information,
        "Privacy observation {PrivacyObservationId}: {Reason}, session {SessionState}, permission {PermissionState}, topology {TopologyRevision}, observed {ObservedTimestamp}, clock {TimestampFrequency}, OS notification delay ms {OsEventToNotificationDelayMilliseconds}.")]
    public static partial void PrivacyObserved(ILogger logger, Guid privacyObservationId,
        WindowsPrivacyChangeReason reason, WindowsSessionState sessionState,
        Kora.Core.Voice.MicrophoneAccessState permissionState, long topologyRevision,
        long observedTimestamp, long timestampFrequency, double? osEventToNotificationDelayMilliseconds);

    [LoggerMessage(209, LogLevel.Information,
        "Privacy query {PrivacyObservationId}: {Reason}, started {QueryStartedTimestamp}, completed {QueryCompletedTimestamp}, clock {TimestampFrequency}.")]
    public static partial void PrivacyQueryCompleted(ILogger logger, Guid privacyObservationId,
        WindowsPrivacyChangeReason reason, long queryStartedTimestamp, long queryCompletedTimestamp,
        long timestampFrequency);

    [LoggerMessage(210, LogLevel.Information,
        "Privacy capture {PrivacyObservationId}, generation {Generation}: recording {WasRecording}, recorder present {HadRecorder}, pending open {HadPendingOpen}, buffers cleared {BuffersClearedTimestamp}, remaining bytes {BufferedBytesAfterClear}, recorder released {RecorderReleasedTimestamp}, confirmed {ReleaseConfirmed}, elapsed ms {ObservedToReleaseMilliseconds}, lock target met {LockReleaseWithinTarget}.")]
    public static partial void CapturePrivacyReleased(ILogger logger, Guid privacyObservationId,
        long generation, bool wasRecording, bool hadRecorder, bool hadPendingOpen, long buffersClearedTimestamp,
        int bufferedBytesAfterClear, long? recorderReleasedTimestamp, bool releaseConfirmed,
        double? observedToReleaseMilliseconds, bool? lockReleaseWithinTarget);

    [LoggerMessage(211, LogLevel.Debug,
        "Rejected stale capture callback {CallbackKind}, generation {CallbackGeneration}, current {CurrentGeneration}.")]
    public static partial void StaleCaptureCallback(ILogger logger, string callbackKind,
        long callbackGeneration, long currentGeneration);

    [LoggerMessage(212, LogLevel.Information,
        "Output generation {Generation}: native playback stopped {PlaybackStoppedTimestamp}, last output sample timestamp unavailable.")]
    public static partial void NativePlaybackStopped(ILogger logger, long generation, long playbackStoppedTimestamp);

    [LoggerMessage(213, LogLevel.Information,
        "Output generation {Generation}: resources released {ResourcesReleasedTimestamp}, audio buffer cleared, bytes before clearing {AudioBytesBeforeClear}.")]
    public static partial void OutputResourcesReleased(ILogger logger, long generation,
        long resourcesReleasedTimestamp, long audioBytesBeforeClear);

    [LoggerMessage(214, LogLevel.Information,
        "Output generation {Generation}: had output {HadOutput}, stop requested {StopRequestedTimestamp}, completion observed {StopCompletedTimestamp}, clock {TimestampFrequency}; completion is not a last-acoustic-sample measurement.")]
    public static partial void OutputStopCompleted(ILogger logger, long generation, bool hadOutput,
        long stopRequestedTimestamp, long stopCompletedTimestamp, long timestampFrequency);

    [LoggerMessage(215, LogLevel.Information,
        "Capture transcript admitted for generation {Generation}; command dispatch requires the separate host gate.")]
    public static partial void CaptureTranscriptAdmitted(ILogger logger, long generation);
}
