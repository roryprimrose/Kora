namespace Kora.Core.Conversation;

/// <summary>Exact optimistic draft comparison rejected before any draft transaction changes.</summary>
public sealed class ConversationDraftConflictException()
    : InvalidOperationException("Review the exact Active session, saved draft body and revision before saving or clearing.");
