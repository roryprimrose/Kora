namespace Kora.Core.Conversation;

public sealed class ConversationCredentialException()
    : InvalidOperationException("Credential-like content was rejected before storage or model admission. Correct the memory-only draft or use a supported secure credential flow. Detection is best-effort, not a secret-free guarantee.");
