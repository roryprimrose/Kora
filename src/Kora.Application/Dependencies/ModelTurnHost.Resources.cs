using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal bool CanSubmitConversation(HostId<SessionIdentity> session)
    {
        lock (dispatchGate)
        {
            return !disposed && !resources.OwnsSession(session)
                && dispatches.All(item => item.Turn.ResourceLease is not null);
        }
    }

    internal async Task<ModelTurnReason> ReserveConversationResourcesAsync(ModelTurn turn,
        Func<bool> admitted, CancellationToken token)
    {
        if (turn.Owner != owner || turn.ResourceLease is not null || !admitted())
        { return ModelTurnReason.HostAdmissionClosed; }
        if (!consumerAdmissions.TryAdd(turn, new(admitted, null)))
        { return ModelTurnReason.HostAdmissionClosed; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        if (await RevalidateContinuationAsync(turn, linked.Token).ConfigureAwait(false) is var reason
            && reason != ModelTurnReason.None) { return reason; }
        lock (dispatchGate)
        {
            if (!CanSubmitConversation(turn.Provenance.Request.SessionId))
            { return ModelTurnReason.ProviderBusy; }
        }
        try
        {
            turn.ResourceLease = await resources.AcquireAsync(turn, () => !disposed && admitted(), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && linked.IsCancellationRequested)
        { return ModelTurnReason.HostAdmissionClosed; }
        if (turn.ResourceLease is null) { return ModelTurnReason.ProviderBusy; }
        lock (dispatchGate)
        {
            return disposed ? ModelTurnReason.HostAdmissionClosed
                : dispatches.Any(item => item.Turn.ResourceLease is null) ? ModelTurnReason.ProviderBusy : ModelTurnReason.None;
        }
    }

    internal bool IsTurnQuiescent(ModelTurn turn) =>
        turn.Owner == owner && turn.ResourceLease is { Joined: true } lease && resources.IsCurrent(lease);

    internal bool ConversationResourcesCurrent(ModelTurn turn) =>
        turn.Owner == owner && Eligible(turn.ControlRevision)
            && turn.ResourceLease is { Joined: false } lease && resources.IsCurrent(lease);

    internal async Task JoinConversationResourcesAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.Owner != owner) { throw new InvalidOperationException("The turn belongs to another runtime owner."); }
        if (turn.ResourceLease is not { } lease)
        { throw new InvalidOperationException("The exact turn has no issued resource lease to join."); }
        if (!turn.ResourceDispatched) { resources.Join(lease, dispatched: false); }
        await lease.Completion.Task.WaitAsync(token).ConfigureAwait(false);
        if (lease.FailedJoin)
        { throw new InvalidOperationException("The exact runtime/resource is quarantined after an unconfirmed physical join."); }
    }
}