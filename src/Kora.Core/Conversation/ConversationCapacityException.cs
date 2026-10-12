namespace Kora.Core.Conversation;

public sealed class ConversationCapacityException()
    : InvalidOperationException("Submitted message capacity is 100 per session; no content was evicted. New sessions have no aggregate storage quota.");
