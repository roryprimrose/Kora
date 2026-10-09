using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

public sealed class SharedSkillAdmission(
    ISessionWorkspaceStore workspace, ISharedSkillSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateSharedSkillSessionAsync(request, eligible, token);

    public Task<T> RunAsync<T>(Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, CancellationToken, Task<T>> operation, CancellationToken token)
    {
        if (HostActivity.Current is not null && HostActivity.RequireCurrent().Request.Origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Shared profile registration and inspection require explicit native local input."); }
        return RunCommittedAsync(RequestOrigin.LocalUi, eligible, async (request, session, currentEligible, admittedToken) =>
        {
            await CommitAsync(request, session, currentEligible, static () => true, admittedToken).ConfigureAwait(false);
            var result = await operation(request, session, admittedToken).ConfigureAwait(false);
            await CommitAsync(request, session, currentEligible, static () => true, admittedToken).ConfigureAwait(false);
            return result;
        }, static _ => HostTaskState.Succeeded, token);
    }

    internal ValueTask<T> CommitAsync<T>(HostRequest request, WorkSessionAuthorization session, Func<bool> eligible,
        Func<T> operation, CancellationToken token) =>
        sessions.WithSharedSkillSessionAsync(request, session.Generation, () =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible() || !OwnsLiveContext(request))
            { throw new InvalidOperationException("Shared skill ownership, session or privacy admission changed."); }
            return operation();
        }, token);
}
