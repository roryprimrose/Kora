using System.Collections.Immutable;

using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed record ConversationWorkObservation(long ControlRevision, long LaneRevision, long LifecycleRevision,
    bool Paused, bool LifecycleHeld, int Accepting, ImmutableArray<RemainingWorkTurn> Turns,
    ConversationExecutionStatus? Running, bool ProgressCurrent)
{
    public ConversationDispatchHoldReason ManualHoldReason { get; init; }
    public bool DispatchHeld => Paused || LifecycleHeld || ManualHoldReason != ConversationDispatchHoldReason.None;
}