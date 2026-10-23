namespace Kora.Core.Conversation;

/// <summary>A whole observed message and explicitly selected role, not model-supplied provenance.</summary>
public sealed record ConversationHistorySelection(ConversationMessage Message, ConversationHistoryRole Role);
