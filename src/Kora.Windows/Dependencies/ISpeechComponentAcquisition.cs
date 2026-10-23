namespace Kora.Windows.Dependencies;

/// <summary>Consented exact-source data transfer, not arbitrary package execution.</summary>
public interface ISpeechComponentAcquisition
{
    /// <summary>Transfers at most the exact declared byte count with no redirects or source substitution.</summary>
    Task DownloadAsync(Kora.Core.Dependencies.SpeechComponentAsset asset, Stream destination,
        CancellationToken cancellationToken);
}
