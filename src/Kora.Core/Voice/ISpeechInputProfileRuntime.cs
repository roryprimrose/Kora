namespace Kora.Core.Voice;

/// <summary>The host-owned activated capture binding, never a provider's routing authority.</summary>
public interface ISpeechInputProfileRuntime
{
    /// <summary>Gets the last committed immutable configuration, even while capture is held unavailable.</summary>
    SpeechInputGeneration ProfileGeneration { get; }
    /// <summary>Gets a value indicating whether the current configuration can admit activated capture.</summary>
    bool IsProfileAvailable { get; }
    /// <summary>Observes host-qualified selectable profiles for the exact current name, without opening capture.</summary>
    IReadOnlyList<SpeechInputProfile> GetProfiles(string assistantName) => SpeechInputProfileCatalogue.Profiles;
    /// <summary>Checks the exact selected tuple and name; null denotes implementation availability, not input consent.</summary>
    string? GetUnavailableReason(SpeechInputSelection selection, string assistantName) =>
        SpeechInputProfileCatalogue.UnavailableReason(selection);
    /// <summary>Binds startup preferences before any capture; null leaves capture explicitly unavailable.</summary>
    void InitializeProfile(SpeechInputSelection? selection);
    /// <summary>Blocks new capture, invalidates old events and joins owned capture/buffer cleanup.</summary>
    Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken);
    /// <summary>Retires input and holds the exact proposed name/profile resources through publication.</summary>
    Task<ISpeechInputProfileSwitch> RetireProfileAsync(SpeechInputSelection selection, string assistantName, CancellationToken cancellationToken) =>
        RetireProfileAsync(cancellationToken);
}
