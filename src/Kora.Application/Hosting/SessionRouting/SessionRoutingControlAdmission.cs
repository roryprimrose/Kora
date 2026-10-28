using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.SessionRouting;

public sealed class SessionRoutingControlAdmission(
    ISessionWorkspaceStore workspace, ISessionRoutingControlStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateRoutingControlSessionAsync(request, eligible, token);

    protected override ValueTask<HostTaskRecord> AdmitOperationAsync(HostTaskRecord intent,
        WorkSessionAuthorization session, Func<bool> eligible, CancellationToken token) =>
        sessions.AdmitLocalContextDispatchAsync(intent.Request, session.Generation, eligible, token);

    protected override async ValueTask RecordCancellationAsync(HostTaskRecord prior) =>
        await Coordinator.RecordOutcomeAsync(prior, prior.State == HostTaskState.DispatchRecorded
            ? HostTaskState.Unknown : HostTaskState.Cancelled, CancellationToken.None).ConfigureAwait(false);

    protected override HostTaskState FailureState(Exception exception) =>
        exception is SessionRoutingInferenceException inference ? inference.Outcome switch
        {
            Kora.Core.Dependencies.ModelTurnOutcome.Denied or Kora.Core.Dependencies.ModelTurnOutcome.DeniedEgress => HostTaskState.Denied,
            Kora.Core.Dependencies.ModelTurnOutcome.Unknown or Kora.Core.Dependencies.ModelTurnOutcome.TimedOut => HostTaskState.Unknown,
            _ => HostTaskState.Unavailable,
        } : base.FailureState(exception);

    internal Task<SessionRoutingReview> RunAsync(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, Func<bool>, CancellationToken, Task<SessionRoutingReview>> operation,
        CancellationToken token) =>
        RunCommittedAsync(origin, eligible, async (request, session, current, admitted) =>
        {
            await Validate(request, session, current, admitted).ConfigureAwait(false);
            var review = await operation(request, session, current, admitted).ConfigureAwait(false);
            await Validate(request, session, current, admitted).ConfigureAwait(false);
            return review;
        }, static review => review.Match == Kora.Core.Conversation.SessionRouting.SessionRouteMatch.Unavailable
            ? HostTaskState.Unavailable : HostTaskState.Succeeded, token);

    private ValueTask<bool> Validate(HostRequest request, WorkSessionAuthorization session,
        Func<bool> eligible, CancellationToken token) =>
        sessions.WithRoutingControlSessionAsync(request, session.Generation, () =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible() || !OwnsLiveContext(request))
            { throw new InvalidOperationException("Routing control session, original channel, privacy or input changed."); }
            return true;
        }, token);
}
