using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Shared original-input and durable-intent admission; each consumer supplies its own session domain.</summary>
public abstract class HostControlAdmission(
    ISessionWorkspaceStore workspace, HostTaskCoordinator coordinator) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private bool evidenceUnavailable;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private WorkSessionAuthorization? session;

    protected abstract ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token);

    protected bool IsDisposed => disposed;
    protected bool IsEvidenceUnavailable => evidenceUnavailable;
    protected void HoldEvidenceUnavailable() => evidenceUnavailable = true;

    protected static RequestOrigin CaptureOriginalOrigin(RequestOrigin origin)
    {
        if (HostActivity.Current is not null) { origin = HostActivity.RequireCurrent().Request.Origin; }
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Preference control requires original local user input.");
        }
        return origin;
    }

    protected static bool OwnsLiveContext(HostRequest request)
    {
        var live = HostActivity.RequireCurrent();
        return !live.Activity!.IsStopped && ReferenceEquals(live.Request, request);
    }

    protected Task<T> RunSynchronousAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, T> operation,
        Func<HostRequest, HostRevision, Func<T>, CancellationToken, ValueTask<T>> withSession,
        CancellationToken cancellationToken) =>
        RunCommittedAsync(origin, eligible, async (request, session, currentEligible, token) =>
            await withSession(request, session.Generation, () =>
            {
                token.ThrowIfCancellationRequested();
                if (!currentEligible() || !OwnsLiveContext(request))
                {
                    throw new InvalidOperationException("Preference input, ownership, privacy or call admission changed.");
                }
                return operation(request, session);
            }, token).ConfigureAwait(false), static _ => HostTaskState.Succeeded, cancellationToken);

    protected async Task<T> RunCommittedAsync<T>(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, Func<bool>, CancellationToken, Task<T>> operation,
        Func<T, HostTaskState> terminalState, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        cancellationToken = linked.Token;
        origin = CaptureOriginalOrigin(origin);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        T admittedResult;
        try
        {
            bool CurrentEligible() => !disposed && !evidenceUnavailable
                && !cancellationToken.IsCancellationRequested && eligible();
            if (!CurrentEligible()) { throw new InvalidOperationException("Preference control host admission is unavailable."); }
            if (session is null)
            {
                var creation = HostRequest.Create(origin);
                using var activity = HostActivity.BeginRoot(creation, HostActivityLayer.Application, HostOperation.Policy);
                var intent = await workspace.RecordControlIntentAsync(creation, cancellationToken).ConfigureAwait(false);
                try { session = await CreateSessionAsync(creation, CurrentEligible, cancellationToken).ConfigureAwait(false); }
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
                var result = await operation(request, session, CurrentEligible, cancellationToken).ConfigureAwait(false);
                var outcome = terminalState(result);
                try { await coordinator.RecordOutcomeAsync(admitted, outcome, CancellationToken.None).ConfigureAwait(false); }
                catch { evidenceUnavailable = true; throw; }
                requestActivity.Complete(outcome == HostTaskState.Succeeded
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
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
        GC.SuppressFinalize(this);
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
