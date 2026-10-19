using System.Diagnostics;
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
    protected ISessionWorkspaceStore Workspace => workspace;
    protected HostTaskCoordinator Coordinator => coordinator;

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

    /// <summary>Serializes exact reviewed requests without manufacturing a different request or session.</summary>
    protected async Task<T> RunExactAsync<T>(HostRequest request, Func<bool> eligible,
        Func<Func<bool>, CancellationToken, Task<T>> operation, Func<T, HostOperationOutcome> outcome,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(current.Request, request) || CaptureOriginalOrigin(request.Origin) != request.Origin)
        {
            throw new InvalidOperationException("Exact local context requires the original live host request.");
        }
        ActivityLink[] links = [new(current.Activity.Context)];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request, links);
            bool CurrentEligible() => !disposed && !evidenceUnavailable
                && !linked.IsCancellationRequested && eligible()
                && !current.Activity.IsStopped && current.Outcome == HostOperationOutcome.Unknown;
            try
            {
                var result = await operation(CurrentEligible, linked.Token).ConfigureAwait(false);
                activity.Complete(outcome(result));
                return result;
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        finally { gate.Release(); }
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
