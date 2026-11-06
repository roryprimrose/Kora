using System.Collections.Immutable;

namespace Kora.Core.Conversation;

/// <summary>Actual joined receipts and remaining host-lane metadata; no rollback or global all-clear.</summary>
public sealed record ConversationPendingClearResult(ConversationPendingClearSnapshot Reviewed,
    ImmutableArray<ConversationPendingClearOutcome> Outcomes,
    ImmutableArray<ConversationPendingClearEntry> RemainingPending,
    ConversationExecutionStatus? Running, bool DispatchHeld, string Outcome);
