using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public static class HostRequestRunner
{
    public static void Run(RequestOrigin origin, Action route,
        HostActivityLayer layer = HostActivityLayer.Application, HostOperation operation = HostOperation.Request)
    {
        ArgumentNullException.ThrowIfNull(route);
        using var request = HostActivity.BeginOperation(layer, operation, origin);
        try
        {
            route();
            request.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            request.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            request.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public static async Task RunAsync(RequestOrigin origin, Func<Task> route,
        HostActivityLayer layer = HostActivityLayer.Application, HostOperation operation = HostOperation.Request)
    {
        ArgumentNullException.ThrowIfNull(route);
        using var request = HostActivity.BeginRoot(HostRequest.Create(origin),
            layer, operation);
        try
        {
            await route().ConfigureAwait(false);
            // Routing completion does not establish that a proposed OS effect occurred.
            request.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            request.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            request.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
