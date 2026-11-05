namespace Kora.Core.Voice;

// A native source range reached by the actual device, bounded by the next native event or audio end.
public sealed record SpeechWordPosition(SpeechSourceProgress Source, TimeSpan PlaybackPosition,
    TimeSpan EndPosition, TimeSpan AudioDuration);
