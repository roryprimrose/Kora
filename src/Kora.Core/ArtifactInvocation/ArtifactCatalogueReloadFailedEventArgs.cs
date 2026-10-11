namespace Kora.Core.Artifacts;

public sealed class ArtifactCatalogueReloadFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}
