namespace Kora.Core.Voice;

public sealed record SpeechPlaybackFrame(bool IsPlaying, double OutputLevel)
{
    public static readonly SpeechPlaybackFrame Inactive = new(false, 0);
}
