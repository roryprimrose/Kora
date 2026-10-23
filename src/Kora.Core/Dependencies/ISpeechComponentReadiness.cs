namespace Kora.Core.Dependencies;

/// <summary>Read-only exact component readiness. This never selects a profile or grants capture authority.</summary>
public interface ISpeechComponentReadiness
{
    /// <summary>Inspects current local bytes without downloading, loading native models or capturing audio.</summary>
    Task<SpeechComponentReadiness> InspectAsync(SpeechComponentIdentity identity, CancellationToken cancellationToken);
}
