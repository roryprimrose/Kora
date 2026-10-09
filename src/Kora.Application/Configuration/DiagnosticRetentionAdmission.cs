using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

public sealed class DiagnosticRetentionAdmission(
    ISessionWorkspaceStore workspace, IDiagnosticRetentionSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateDiagnosticRetentionSessionAsync(request, eligible, token);

    public Task<T> RunAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, T> operation, CancellationToken token) =>
        RunSynchronousAsync(origin, eligible, operation, sessions.WithDiagnosticRetentionSessionAsync, token);
}
