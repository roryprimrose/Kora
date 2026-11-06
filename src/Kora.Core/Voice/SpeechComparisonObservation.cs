namespace Kora.Core.Voice;

/// <summary>Measured open-to-start latency; unsupported acoustic/cache/resource measurements remain unknown.</summary>
public sealed record SpeechComparisonObservation(string FinalText, TimeSpan OpenToStart)
{
    public const int MaximumFinalTextLength = 256;
    public const int MaximumProfiles = 2;
    public static TimeSpan MaximumAttempt { get; } = TimeSpan.FromSeconds(10);
    public TimeSpan? EndOfSpeechToFinal => null;
    public TimeSpan? ColdStartup => null;
    public TimeSpan? WarmStartup => null;
    public long? ResourceBytes => null;
}
