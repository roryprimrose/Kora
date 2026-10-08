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

    protected override ValueTask<T> WithSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken token) =>
        sessions.WithAudioControlSessionAsync(request, generation, operation, token);
}