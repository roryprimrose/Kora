using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Tools.Application;

public sealed class ApplicationUpgrade(IApplicationMaintenance maintenance)
{
    public const string Id = "application.upgrade";

    public Task<string> ExecuteAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken) =>
        maintenance.RequestUpgradeAsync(version, origin, admitted, cancellationToken);
}
