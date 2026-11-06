namespace Kora.Core.Voice;

/// <summary>The observed terminal of one exact read; none of these outcomes permits replay.</summary>
public enum SpeechReadOutcome
{
    /// <summary>The complete admitted playback and its terminal audit completed.</summary>
    Played,
    /// <summary>Source or speech admission refused playback.</summary>
    Denied,
    /// <summary>The read was cancelled or retired without a complete playback.</summary>
    Cancelled,
    /// <summary>The speech provider reported a playback failure.</summary>
    Failed,
    /// <summary>Playback, resource release or required evidence could not be confirmed.</summary>
    Unknown,
}
