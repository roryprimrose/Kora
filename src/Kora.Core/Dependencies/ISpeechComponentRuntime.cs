namespace Kora.Core.Dependencies;

/// <summary>The installed host adapter owns compatibility, inert functional checks and all runtime consumers.</summary>
public interface ISpeechComponentRuntime
{
    /// <summary>Reads installed compatibility and existing authoritative functional evidence; never loads, probes, captures or downloads.</summary>
    SpeechComponentRuntimeObservation Observe(SpeechComponentDefinition component);
    /// <summary>Excludes new consumers and joins existing users, or returns an explicit unavailable, busy, unknown or ownership denial.</summary>
    ValueTask<SpeechComponentMutationAdmission> TryAcquireExclusiveAsync(SpeechComponentDefinition component, CancellationToken cancellationToken);
    /// <summary>With exclusive joined ownership, holds future asset consumers unavailable without changing saved choice or consent.</summary>
    void HoldUnavailable(SpeechComponentDefinition component);
    /// <summary>Runs the exact adapter's inert local fixture without microphone, inference commands or preference changes.</summary>
    Task<SpeechComponentRuntimeObservation> VerifyAsync(SpeechComponentDefinition component, CancellationToken cancellationToken);
}