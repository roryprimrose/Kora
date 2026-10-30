namespace Kora.Core.Voice;

// Host evidence only: positions refer to the exact unmodified synthesis source and device clock.
public sealed record SpeechSentencePosition(SpeechSourceProgress Source,
    TimeSpan PlaybackPosition, TimeSpan AudioDuration);
