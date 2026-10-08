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

    protected override ValueTask<T> WithSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken token) =>
        sessions.WithDiagnosticRetentionSessionAsync(request, generation, operation, token);
}
