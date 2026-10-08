using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Communication;

public sealed class ManualCallControl(
    ISessionWorkspaceStore workspace, IManualCallControlStore store, HostTaskCoordinator coordinator) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private WorkSessionAuthorization? session;
    private bool disposed;
    private bool evidenceUnavailable;

    public async Task<CallMutationOutcome> SetAsync(bool active, RequestOrigin originalOrigin,
        long revision, CallCommunicationPolicy policy, Func<bool> admitted, Func<ManualCallRetirement> retire,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        originalOrigin = HostActivity.Current?.Request.Origin ?? originalOrigin;
        if (originalOrigin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Manual call requires original local user input.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool Eligible() => !disposed && !evidenceUnavailable && !token.IsCancellationRequested && admitted();
            if (!Eligible()) { return CallMutationOutcome.HostUnavailable; }
            if (session is null)
            {
                var creation = HostRequest.Create(originalOrigin);
                using var creationActivity = HostActivity.BeginRoot(creation, HostActivityLayer.Application, HostOperation.Policy);
                try
                {
                    var creationIntent = await workspace.RecordControlIntentAsync(creation, token).ConfigureAwait(false);
                    session = await store.CreateManualCallControlSessionAsync(creation, Eligible, token).ConfigureAwait(false);
                    await coordinator.RecordOutcomeAsync(creationIntent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    evidenceUnavailable = true;
                    policy.HoldManualControlEvidenceUnavailable();
                    creationActivity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
                creationActivity.Complete(HostOperationOutcome.Completed);
            }
            var request = new HostRequest(new(Guid.NewGuid()), session.SessionId, new(Guid.NewGuid()), originalOrigin);
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Policy);
            try
            {
                var intent = await workspace.RecordControlIntentAsync(request, token).ConfigureAwait(false);
                bool OwnContext()
                {
                    var live = HostActivity.RequireCurrent();
                    return !live.Activity!.IsStopped && ReferenceEquals(live.Request, request);
                }
                var outcome = await store.ApplyManualCallAsync(request, session.Generation, active, Eligible, async () =>
                {
                    Task retirement = Task.CompletedTask;
                    Func<bool> retiredEligible = admitted;
                    var transition = policy.CommitManual(active, request.Origin, revision,
                        () => Eligible() && OwnContext(),
                        () =>
                        {
                            var fenced = retire();
                            retirement = fenced.Completion;
                            retiredEligible = fenced.RemainsEligible;
                        },
                        () => !disposed && !evidenceUnavailable && !token.IsCancellationRequested
                            && OwnContext() && retiredEligible());
                    await retirement.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return transition;
                }, token).ConfigureAwait(false);
                await coordinator.RecordOutcomeAsync(intent,
                    outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                        ? HostTaskState.Succeeded : HostTaskState.Denied, CancellationToken.None).ConfigureAwait(false);
                activity.Complete(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                return outcome;
            }
            catch
            {
                // The process-memory effect may already have happened. Do not certify rollback,
                // retry, substitute a fresh session or issue further mutations after a lost receipt.
                evidenceUnavailable = true;
                policy.HoldManualControlEvidenceUnavailable();
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        finally { gate.Release(); }
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
