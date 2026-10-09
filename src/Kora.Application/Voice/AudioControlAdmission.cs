using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Hosting;

namespace Kora.Application.Voice;

public sealed class AudioControlAdmission(
    ISessionWorkspaceStore workspace, IAudioControlSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateAudioControlSessionAsync(request, eligible, token);

    public Task<T> RunAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, T> operation, CancellationToken token) =>
        RunSynchronousAsync(origin, eligible, operation, sessions.WithAudioControlSessionAsync, token);
}