using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Hosting;

namespace Kora.Application.Voice;

public sealed class AudioControlAdmission(
    ISessionWorkspaceStore workspace, IAudioControlSessionStore sessions, HostTaskCoordinator coordinator) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private bool evidenceUnavailable;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private WorkSessionAuthorization? session;

    public async Task<T> RunAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, T> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        cancellationToken = linked.Token;
        if (HostActivity.Current is not null) { origin = HostActivity.RequireCurrent().Request.Origin; }
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Audio control requires original local user input.");
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        T admittedResult;
        try
        {
            if (disposed || evidenceUnavailable || !eligible()) { throw new InvalidOperationException("Audio control host admission is unavailable."); }
            if (session is null)
            {
                var creation = HostRequest.Create(origin);
                using var activity = HostActivity.BeginRoot(creation, HostActivityLayer.Application, HostOperation.Policy);
                var intent = await workspace.RecordControlIntentAsync(creation, cancellationToken).ConfigureAwait(false);
                try { session = await sessions.CreateAudioControlSessionAsync(creation, eligible, cancellationToken).ConfigureAwait(false); }
                catch { evidenceUnavailable = true; throw; }
                try { await coordinator.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false); }
                catch { evidenceUnavailable = true; throw; }
                activity.Complete(HostOperationOutcome.Completed);
            }
            var request = new HostRequest(new(Guid.NewGuid()), session.SessionId, new(Guid.NewGuid()), origin);
            using var requestActivity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var admitted = await workspace.RecordControlIntentAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await sessions.WithAudioControlSessionAsync(request, session.Generation, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (disposed || !eligible() || !ReferenceEquals(HostActivity.RequireCurrent().Request, request))
                    {
                        throw new InvalidOperationException("Audio control input, ownership, privacy or call admission changed.");
                    }
                    return operation(request, session);
                }, cancellationToken).ConfigureAwait(false);
                try { await coordinator.RecordOutcomeAsync(admitted, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false); }
                catch { evidenceUnavailable = true; throw; }
                requestActivity.Complete(HostOperationOutcome.Completed);
                admittedResult = result;
            }
            catch (OperationCanceledException)
            {
                requestActivity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                await coordinator.RecordOutcomeAsync(admitted, HostTaskState.Failed, CancellationToken.None).ConfigureAwait(false);
                requestActivity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        finally { gate.Release(); }
        return admittedResult;
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        gate.Dispose();
        lifetime.Dispose();
    }

}