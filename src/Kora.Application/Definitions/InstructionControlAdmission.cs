using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Definitions;

public sealed class InstructionControlAdmission(
    ISessionWorkspaceStore workspace, IInstructionControlSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateInstructionSession(request, eligible, token);

    public Task<T> Run<T>(Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, Func<bool>, CancellationToken, Task<T>> operation, CancellationToken token) =>
        RunCommittedAsync(RequestOrigin.LocalUi, eligible, async (request, session, current, admittedToken) =>
        {
            await Commit(request, session, current, admittedToken).ConfigureAwait(false);
            var result = await operation(request, session, current, admittedToken).ConfigureAwait(false);
            await Commit(request, session, current, admittedToken).ConfigureAwait(false);
            return result;
        }, static _ => HostTaskState.Succeeded, token);

    private ValueTask<bool> Commit(HostRequest request, WorkSessionAuthorization session, Func<bool> eligible, CancellationToken token) =>
        Publish(request, session, eligible, static () => true, token);

    internal ValueTask<T> Publish<T>(HostRequest request, WorkSessionAuthorization session,
        Func<bool> eligible, Func<T> operation, CancellationToken token) =>
        sessions.WithInstructionSession(request, session.Generation, () =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible() || !OwnsLiveContext(request))
            { throw new InvalidOperationException("Instruction ownership, session, original input or privacy changed."); }
            return operation();
        }, token);
}
