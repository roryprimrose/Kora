using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.Maintenance;

public interface IMaintenanceCommands
{
    Task<string> ExecuteAsync(MaintenanceCommand command, RequestOrigin origin, Func<bool> eligible,
        CancellationToken cancellationToken);
}
