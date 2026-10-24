namespace Kora.Core.Voice;

public static class VoiceInputLimits
{
    public const int MaximumTranscriptCharacters = 4096;
    public static TimeSpan MaximumCommandDuration { get; } = TimeSpan.FromSeconds(60);
}
