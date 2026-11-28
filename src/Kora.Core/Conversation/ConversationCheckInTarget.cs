namespace Kora.Core.Conversation;

/// <summary>The exact running turn and pending check-in that one answer may resolve; display text cannot select it.</summary>
public sealed record ConversationCheckInTarget(ConversationCancellationTarget Execution, ConversationCheckIn CheckIn);
