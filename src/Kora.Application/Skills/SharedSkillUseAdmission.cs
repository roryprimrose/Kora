using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Original-channel freshness admission for reviewed use, never native source registration.</summary>
public sealed class SharedSkillUseAdmission(
    ISessionWorkspaceStore workspace, ISharedSkillSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateSharedSkillSessionAsync(request, eligible, token);

    internal Task<T> RunAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, Func<bool>, CancellationToken, Task<T>> operation, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        return RunCommittedAsync(origin, eligible, operation, static _ => HostTaskState.Succeeded, token);
    }

    internal ValueTask<T> CommitAsync<T>(HostRequest request, WorkSessionAuthorization session, Func<bool> eligible,
        Func<T> operation, CancellationToken token) =>
        sessions.WithSharedSkillSessionAsync(request, session.Generation, () =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible() || !OwnsLiveContext(request))
            { throw new InvalidOperationException("Reviewed skill use lost its original host authority."); }
            return operation();
        }, token);
}
