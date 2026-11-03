namespace Kora.Core.Voice;

public sealed record SpeechPlaybackFrame(bool IsPlaying, double OutputLevel,
    Guid? PlaybackId = null, long Generation = 0, int Segment = 0,
    SpeechSentencePosition? Sentence = null, bool SentenceAlignmentAvailable = false,
    SpeechWordPosition? Word = null, bool WordAlignmentAvailable = false)
{
    public static readonly SpeechPlaybackFrame Inactive = new(false, 0);
}
