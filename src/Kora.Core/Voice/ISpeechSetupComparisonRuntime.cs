namespace Kora.Core.Voice;

/// <summary>Exact installed and qualified inert-transcription capabilities, independent of command routing.</summary>
public interface ISpeechSetupComparisonRuntime
{
    /// <summary>Returns only currently compatible installed profiles admitted for this operation.</summary>
    IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles();

    /// <summary>Exclusively reserves quiescent input without changing listening or profile preferences.</summary>
    Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
        MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken);
}
