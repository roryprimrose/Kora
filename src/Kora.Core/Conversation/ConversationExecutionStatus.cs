using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Volatile lifecycle observation, not a durable task receipt or proof of cancellation.</summary>
public sealed record ConversationExecutionStatus(ConversationCancellationTarget Target,
    ConversationExecutionPhase Phase, ModelTurnOutcome? Outcome = null, HostTaskState? TaskState = null,
    ConversationExecutionProgress? Progress = null);
