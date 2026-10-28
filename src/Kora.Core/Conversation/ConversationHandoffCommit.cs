namespace Kora.Core.Conversation;

public sealed record ConversationHandoffCommit(ConversationSubmissionReceipt Source, ConversationSubmissionReceipt Successor);
