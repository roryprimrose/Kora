using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Tools.Application;

public sealed class ApplicationCheckForUpdates(IApplicationMaintenance maintenance)
{
    public const string Id = "application.check_for_updates";

    public Task<string> ExecuteAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken) =>
        maintenance.CheckForUpdatesAsync(version, origin, admitted, cancellationToken);
}
