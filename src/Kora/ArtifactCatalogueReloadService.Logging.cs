using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ArtifactCatalogueReloadService
{
    [LoggerMessage(2600, LogLevel.Information,
        "Published a reloaded artifact catalogue containing {ArtifactCount} artifacts")]
    private static partial void ArtifactReloaded(ILogger logger, int artifactCount);

    [LoggerMessage(2601, LogLevel.Error,
        "Artifact catalogue reload failed; owned instructions are unavailable; failure {FailureType}")]
    private static partial void ArtifactReloadFailed(ILogger logger, string failureType);

    [LoggerMessage(2602, LogLevel.Error,
        "Artifact source watcher failed; watcher registration and reload will be retried; failure {FailureType}")]
    private static partial void ArtifactWatcherFailed(ILogger logger, string failureType);
}
