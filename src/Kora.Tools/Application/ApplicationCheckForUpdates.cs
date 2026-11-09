using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;

namespace Kora.Tools.Application;

public sealed class ApplicationCheckForUpdates(IApplicationMaintenance maintenance)
{
    public const string Id = MaintenanceTools.Check;

    public Task<string> ExecuteAsync(ReleaseVersion? version, RequestOrigin origin,
        Func<bool> admitted, CancellationToken cancellationToken) =>
        maintenance.CheckForUpdatesAsync(version, origin, admitted, cancellationToken);

    public ValueTask<CapabilityReply> ExecuteMcpAsync(string input, Func<bool> admitted, CancellationToken token) =>
        maintenance is IApplicationMaintenanceMcp tools ? tools.InvokeAsync(Id, input, admitted, token)
            : ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Denied, "maintenance-mcp-unavailable"));
}
