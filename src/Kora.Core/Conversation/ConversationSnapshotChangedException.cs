namespace Kora.Core.Conversation;

public sealed class ConversationSnapshotChangedException()
    : InvalidOperationException("A host terminal status changed within the conversation snapshot; refresh without replay.");
