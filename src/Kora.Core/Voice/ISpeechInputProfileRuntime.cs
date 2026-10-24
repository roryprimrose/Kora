namespace Kora.Core.Voice;

/// <summary>The host-owned activated capture binding, never a provider's routing authority.</summary>
public interface ISpeechInputProfileRuntime
{
    /// <summary>Gets the last committed immutable configuration, even while capture is held unavailable.</summary>
    SpeechInputGeneration ProfileGeneration { get; }
    /// <summary>Gets a value indicating whether the current configuration can admit activated capture.</summary>
    bool IsProfileAvailable { get; }
    /// <summary>Binds startup preferences before any capture; null leaves capture explicitly unavailable.</summary>
    void InitializeProfile(SpeechInputSelection? selection);
    /// <summary>Blocks new capture, invalidates old events and joins owned capture/buffer cleanup.</summary>
    Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken);
}
