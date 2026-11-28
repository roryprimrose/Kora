using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Volatile lifecycle observation, not a durable task receipt or proof of cancellation.</summary>
/// <param name="CheckIn">The pending ordinary check-in, if the running turn is waiting for an optional answer.</param>
public sealed record ConversationExecutionStatus(ConversationCancellationTarget Target,
    ConversationExecutionPhase Phase, ModelTurnOutcome? Outcome = null, HostTaskState? TaskState = null,
    ConversationExecutionProgress? Progress = null, ConversationResourceStatus? Resources = null,
    ConversationCheckIn? CheckIn = null);
