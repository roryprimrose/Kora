using System.Runtime.CompilerServices;

using Kora.Application.Agents;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private sealed record DispatchLease(ModelTurn Turn, LocalAgentExecutionLane? Lane, TaskCompletionSource Released);
    private readonly ConditionalWeakTable<ModelTurn, LocalAgentExecutionLane> agentLanes = new();
    private readonly List<DispatchLease> dispatches = [];
    private readonly List<Exception> lateFailures = [];
    private long dispatchRevision;

    internal LocalAgentExecutionLane IssueLocalAgentLane(BoundedAgentReview review, ModelTurn turn,
        ILocalAgentConcurrencyQualification qualification)
    {
        if (turn.Owner != owner || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || turn.Provenance.Request.SessionId != review.Target.Authority.SessionId
            || turn.SessionGeneration != review.Target.Authority.Generation
            || turn.Provenance.Request.Origin != review.Origin
            || !string.Equals(turn.Context.UserRequest, review.Input, StringComparison.Ordinal)
            || turn.Context.ToolMode != ModelToolMode.None || turn.Context.Constraints is null
            || !turn.Context.Serialize().AsSpan().SequenceEqual(turn.Wire))
        { throw new InvalidOperationException("The original reviewed local agent request does not match canonical admission."); }
        var lane = new LocalAgentExecutionLane(this, review, turn, qualification);
        agentLanes.Add(turn, lane);
        return lane;
    }

    internal bool CanSubmitLocalAgent(BoundedAgentReview review, ILocalAgentConcurrencyQualification qualification)
    {
        lock (dispatchGate)
        {
            if (disposed) { return false; }
            if (dispatches.Count == 0) { return IsQuiescent; }
            if (dispatches.Count >= 2 || dispatches.Any(item => item.Lane is null
                || item.Turn.Provenance.Request.SessionId == review.Target.Authority.SessionId)) { return false; }
            var revision = dispatchRevision;
            var available = dispatches.ToArray().All(item => ReferenceEquals(item.Lane!.Qualification, qualification)
                && CapacityCurrent(item.Lane));
            return available && !disposed && dispatchRevision == revision;
        }
    }

    private bool CapacityCurrent(LocalAgentExecutionLane lane) =>
        ReferenceEquals(lane.Host, this) && lane.Capacity is { MaximumRuns: 2 } capacity
        && capacity.Revision != Guid.Empty && capacity.Selection == lane.Turn.Provenance.Selection
        && ReferenceEquals(capacity.Adapter, lane.Turn.Registration.Adapter)
        && capacity.Profile == lane.Turn.Profile && capacity.ObservedAt <= time.GetUtcNow()
        && time.GetUtcNow() < capacity.ExpiresAt && lane.Qualification.IsCurrent(capacity)
        && lane.Turn.Registration.IsQualified(time.GetUtcNow());

    private DispatchLease? AcquireDispatch(ModelTurn turn)
    {
        lock (dispatchGate)
        {
            if (disposed) { return null; }
            var revision = dispatchRevision;
            agentLanes.TryGetValue(turn, out var lane);
            if (lane is not null)
            { lane.Capacity = lane.Qualification.Observe(turn.Provenance.Selection, turn.Profile, turn.Registration.Adapter!); }
            if (dispatches.Count == 0)
            {
                if (!IsQuiescent) { return null; }
            }
            else if (dispatches.Count >= 2 || lane is null || !CapacityCurrent(lane)
                || dispatches.ToArray().Any(item => item.Lane is null
                    || item.Turn.Provenance.Request.SessionId == turn.Provenance.Request.SessionId
                    || !ReferenceEquals(item.Lane.Qualification, lane.Qualification)
                    || item.Lane.Capacity != lane.Capacity || !CapacityCurrent(item.Lane)))
            { return null; }
            if (disposed || dispatchRevision != revision) { return null; }
            if (dispatches.Count == 0) { quiescence = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            var lease = new DispatchLease(turn, lane, new(TaskCreationOptions.RunContinuationsAsynchronously));
            dispatches.Add(lease);
            dispatchRevision++;
            if (lane is not null) { lane.Completion = lease.Released.Task; }
            return lease;
        }
    }

    private void ReleaseDispatch(DispatchLease lease)
    {
        lock (dispatchGate)
        {
            dispatches.Remove(lease);
            dispatchRevision++;
            lease.Released.TrySetResult();
            if (dispatches.Count == 0) { quiescence!.TrySetResult(); }
        }
    }
}
