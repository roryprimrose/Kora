using Kora.Application.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal async Task<SessionWorkspaceEntry> CreateLocalContextSessionAsync(
        HostRequest sourceRequest, Func<bool> eligible, CancellationToken token)
    {
        var original = HostActivity.RequireCurrent();
        if (!ReferenceEquals(original.Request, sourceRequest) || original.Activity!.IsStopped
            || original.Outcome != HostOperationOutcome.Unknown)
        {
            throw new InvalidOperationException("Local context creation requires the exact original source request.");
        }
        // Session creation has its own genuine control task, never the source-turn task or source content.
        var creation = new HostRequest(new(Guid.NewGuid()), sourceRequest.SessionId, new(Guid.NewGuid()), sourceRequest.Origin);
        // Creation may commit before cancellation is observed. Its task must close without claiming rollback or retry.
        return await ControlAsync(sourceRequest.SessionId, sourceRequest.Origin,
            (request, current) => store.CreateNamedSessionAsync(request,
                new SessionName(LocalContextSessionTarget.FreshSessionName), current, token),
            token, eligible, originalRequest: creation, finalizeIncompleteOnFailure: true).ConfigureAwait(false);
    }
}