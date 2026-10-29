namespace Kora.Core.Voice;

// Only the matching native prompt can produce this evidence; callback-rendered text is not authority.
public sealed record SpeechSourceProgress(SpeechPlaybackToken Playback,
    int CharacterPosition, int CharacterCount, TimeSpan AudioPosition);
