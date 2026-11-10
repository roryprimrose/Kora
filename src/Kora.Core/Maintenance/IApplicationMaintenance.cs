using Kora.Core.Hosting;

namespace Kora.Core.Maintenance;

/// <summary>Governed original-user check and installer-launch requests; neither grants installer approval.</summary>
public interface IApplicationMaintenance
{
    /// <summary>Checks the latest or exact published version using already-consented network access.</summary>
    Task<string> CheckForUpdatesAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken);

    /// <summary>Requests native exact-release approval, then starts the verified installer; setup owns shutdown and installation.</summary>
    Task<string> RequestUpgradeAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken);
}
