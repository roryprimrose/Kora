namespace Kora.Core.Conversation;

/// <summary>Original submitted intent, not unsent composer text or renewed execution authority.</summary>
public sealed record ConversationPendingTurn(ConversationSubmissionReceipt Submission,
    DateTimeOffset ExpiresAt, ConversationPendingPhase Phase, int Position);
