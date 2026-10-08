using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Communication;

public sealed class ManualCallControl(
    ISessionWorkspaceStore workspace, IManualCallControlStore store, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        store.CreateManualCallControlSessionAsync(request, eligible, token);

    public async Task<CallMutationOutcome> SetAsync(bool active, RequestOrigin originalOrigin,
        long revision, CallCommunicationPolicy policy, Func<bool> admitted, Func<ManualCallRetirement> retire,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        originalOrigin = CaptureOriginalOrigin(originalOrigin);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsEvidenceUnavailable || !admitted()) { return CallMutationOutcome.HostUnavailable; }
        try
        {
            return await RunCommittedAsync(originalOrigin, admitted,
                (request, session, eligible, token) =>
                    store.ApplyManualCallAsync(request, session.Generation, active, eligible, async () =>
                {
                    Task retirement = Task.CompletedTask;
                    Func<bool> retiredEligible = admitted;
                    var transition = policy.CommitManual(active, request.Origin, revision,
                        () => eligible() && OwnsLiveContext(request),
                        () =>
                        {
                            var fenced = retire();
                            retirement = fenced.Completion;
                            retiredEligible = fenced.RemainsEligible;
                        },
                        () => !IsDisposed && !token.IsCancellationRequested
                            && OwnsLiveContext(request) && retiredEligible());
                    await retirement.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return transition;
                }, token).AsTask(), static outcome =>
                    outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                        ? HostTaskState.Succeeded : HostTaskState.Denied, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The process-memory effect may already have happened. Do not certify rollback,
            // retry, substitute a fresh session or issue further mutations after a lost receipt.
            HoldEvidenceUnavailable();
            policy.HoldManualControlEvidenceUnavailable();
            throw;
        }
    }
}
