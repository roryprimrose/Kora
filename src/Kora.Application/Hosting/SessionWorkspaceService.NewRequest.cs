using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public async Task<SessionWorkspaceEntry> CreateAsync(NewRequestCreationAttempt attempt, Func<bool> admitted, CancellationToken token)
    {
        if (HostActivity.HasScope && HostActivity.Current is not { Request.Origin: RequestOrigin.LocalUi })
        { throw new InvalidOperationException("Fresh target creation requires original native intent."); }
        var revision = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == revision && admitted();
        ActivityLink link = default;
        var created = await ControlAsync(attempt.Request.SessionId, RequestOrigin.LocalUi,
            (request, eligible) =>
            {
                link = new(HostActivity.RequireCurrent().Activity!.Context);
                return store.CreateNamedSessionAsync(request, attempt.Name, eligible, token);
            }, token, Live, originalRequest: attempt.Request).ConfigureAwait(false);
        using var activity = HostActivity.BeginRoot(attempt.Request, HostActivityLayer.Application, HostOperation.Storage, [link]);
        try
        {
            attempt.RequireExactTarget(created);
            var actual = await store.ReadMetadataAsync(attempt.Request.SessionId, token).ConfigureAwait(false);
            var observed = await store.ReadTaskAsync(attempt.Request.SessionId, attempt.Request.TaskId, token).ConfigureAwait(false);
            if (actual != created || observed?.Task.Request != attempt.Request
                || observed.Task.State != HostTaskState.Succeeded || observed.Task.Revision.Value != 2 || !Live())
            { throw new IOException("Fresh target creation completion is unconfirmed; inspect the exact target/request/task without retry."); }
            activity.Complete(HostOperationOutcome.Completed);
            return created;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public Task<SessionWorkspaceEntry> ReadExactMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => store.ReadMetadataAsync(session, token), token);

    public Task<HostTaskObservation?> ReadExactTaskAsync(HostRequest request, CancellationToken token) =>
        ReadAsync(() => store.ReadTaskAsync(request.SessionId, request.TaskId, token), token);
}
