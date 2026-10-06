using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public static class HostRequestRunner
{
    public static async Task RunAsync(RequestOrigin origin, Func<Task> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        using var request = HostActivity.BeginRoot(HostRequest.Create(origin),
            HostActivityLayer.Application, HostOperation.Request);
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
