using Kora.Core.Hosting;

namespace Kora.Core.Maintenance;

/// <summary>Governed original-user check and upgrade-review actions; neither grants installer approval.</summary>
public interface IApplicationMaintenance
{
    /// <summary>Checks the latest or exact published version using already-consented network access.</summary>
    Task<string> CheckForUpdatesAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken);

    /// <summary>Prepares exact upgrade information for separate native review and installer approval.</summary>
    Task<string> RequestUpgradeAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken);
}
