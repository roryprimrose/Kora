namespace Kora.Core.Conversation;

public sealed record ConversationSearchCursor(ConversationCursor Conversation, string QueryDigest,
    ConversationSearchRole Role = ConversationSearchRole.All, ConversationTerminalFilter Terminal = default);
