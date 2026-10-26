namespace Kora.Core.Communication;

/// <summary>Metadata-only platform seam; supplies no speech, capture or settings authority.</summary>
public interface ICommunicationActivitySource : IAsyncDisposable
{
    /// <summary>Whether the exact production profile has qualified local support.</summary>
    bool IsQualified { get; }

    /// <summary>Whether a retired native resource is still owned and must not be replaced.</summary>
    bool HasUnreleasedResources { get; }

    /// <summary>
    /// Observes one generation until cancellation, returning only after owned resources have
    /// joined. Failed release retains resource ownership and must be retried by disposal.
    /// </summary>
    Task ObserveAsync(Guid generation, Action<CommunicationActivityObservation> publish, CancellationToken token);
}