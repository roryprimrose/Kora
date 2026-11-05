using Kora.Core.Maintenance;

namespace Kora.Application.Maintenance;

/// <summary>Obtains the exact installer asset bound to a verified canonical release.</summary>
public interface IReleaseInstallerDownloader
{
    /// <summary>Returns a verified download whose lease prevents replacement during launch.</summary>
    Task<IReleaseInstallerDownload> DownloadAsync(ReleaseMetadata release, CancellationToken cancellationToken);
}
