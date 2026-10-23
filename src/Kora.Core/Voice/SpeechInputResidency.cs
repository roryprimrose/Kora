namespace Kora.Core.Voice;

/// <summary>Requested model lifetime; never microphone permission or audio retention.</summary>
public enum SpeechInputResidency
{
    /// <summary>Release recognition resources with the activated capture.</summary>
    CaptureLifetime,
    /// <summary>Keep model resources loaded, unavailable for the current adapter.</summary>
    KeepLoaded,
}
