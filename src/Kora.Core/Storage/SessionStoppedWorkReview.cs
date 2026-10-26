using Kora.Core.Conversation;

namespace Kora.Core.Storage;

/// <summary>The exact dispatched original reviewed for cancellation and subsequent logical disposition.</summary>
public sealed record SessionStoppedWorkReview(ConversationCancellationTarget Target, ConversationMessage Message);
