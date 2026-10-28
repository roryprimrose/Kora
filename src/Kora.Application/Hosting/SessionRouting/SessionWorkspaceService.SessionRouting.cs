using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal async Task<SessionWorkspaceEntry> CreateRoutingTargetAsync(HostRequest original, Func<bool> admitted, CancellationToken token)
    {
        var created = await ControlAsync(original.SessionId, original.Origin,
            (request, eligible) => store.CreateNamedSessionAsync(request, new("New conversation"), eligible, token),
            token, admitted, originalRequest: original).ConfigureAwait(false);
        using var activity = HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Storage);
        try
        {
            var actual = await ReadExactMetadataAsync(original.SessionId, token).ConfigureAwait(false);
            var task = await ReadExactTaskAsync(original, token).ConfigureAwait(false);
            if (actual != created || created.Authority.SessionId != original.SessionId || !created.Authority.IsActive
                || task?.Task.Request != original || task.Task.State != HostTaskState.Succeeded
                || task.Task.Revision.Value != 2 || !admitted())
            { throw new IOException("Fresh routing target creation is unconfirmed. Inspect its exact request/session; never retry automatically."); }
            activity.Complete(HostOperationOutcome.Completed);
            return created;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }
}
