namespace Kora.Core.Conversation;

/// <summary>The only retained conversation roles eligible as inert evidence.</summary>
public enum ConversationHistoryRole
{
    /// <summary>Exact original user text.</summary>
    User,
    /// <summary>Exact finalized assistant answer, never host status.</summary>
    Assistant,
}
