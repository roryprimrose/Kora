using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

public sealed class AuditRetentionAdmission(
    ISessionWorkspaceStore workspace, IAuditRetentionSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateAuditRetentionSessionAsync(request, eligible, token);

    public Task<T> RunAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, T> operation, CancellationToken token) =>
        RunSynchronousAsync(origin, eligible, operation, sessions.WithAuditRetentionSessionAsync, token);
}
