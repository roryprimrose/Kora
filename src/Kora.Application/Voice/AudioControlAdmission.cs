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

    public Task<T> RunObservedAsync<TObservation, T>(RequestOrigin origin, Func<bool> eligible,
        Func<CancellationToken, Task<TObservation>> observe,
        Func<HostRequest, WorkSessionAuthorization, TObservation, T> publish, CancellationToken token) =>
        RunCommittedAsync(origin, eligible, async (request, session, currentEligible, cancellation) =>
        {
            var observation = await observe(cancellation).ConfigureAwait(false);
            return await sessions.WithAudioControlSessionAsync(request, session.Generation, () =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (!currentEligible() || !OwnsLiveContext(request))
                { throw new InvalidOperationException("Provider observation lost original input, ownership, privacy or session admission."); }
                return publish(request, session, observation);
            }, cancellation).ConfigureAwait(false);
        }, static _ => HostTaskState.Succeeded, token);
}