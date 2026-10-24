namespace Kora.Core.Voice;

// Host evidence only: positions refer to the exact unmodified synthesis source and device clock.
public sealed record SpeechSentencePosition(int Index, int Start, int Length,
    TimeSpan AudioStart, TimeSpan PlaybackPosition, TimeSpan AudioDuration);
