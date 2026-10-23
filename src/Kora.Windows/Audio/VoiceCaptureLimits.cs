namespace Kora.Windows.Audio;

internal sealed record VoiceCaptureLimits(
    TimeSpan OpenDeadline,
    TimeSpan MaximumCapture,
    TimeSpan EmptySpeechDeadline,
    TimeSpan RecognitionDeadline,
    int MaximumTranscriptCharacters)
{
    public static VoiceCaptureLimits Default { get; } = new(
        TimeSpan.FromSeconds(5),
        Kora.Core.Voice.VoiceInputLimits.MaximumCommandDuration,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        Kora.Core.Voice.VoiceInputLimits.MaximumTranscriptCharacters);
}
