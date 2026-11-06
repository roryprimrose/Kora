namespace Kora.Core.Voice;

/// <summary>Supplies a confirmed immutable snapshot; unavailable saved state must refuse capture.</summary>
public interface IVoiceCaptureTimingSource
{
    /// <summary>Gets timing for one new capture; grants no input authority.</summary>
    VoiceCaptureTiming GetTiming();
}
