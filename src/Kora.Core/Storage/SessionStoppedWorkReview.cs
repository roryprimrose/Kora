using Kora.Core.Conversation;
using Kora.Core.Dependencies;

namespace Kora.Core.Storage;

/// <summary>The exact dispatched original reviewed for cancellation and subsequent logical disposition.</summary>
public sealed record SessionStoppedWorkReview(ConversationCancellationTarget Target, ConversationMessage Message)
{
    public SessionModelUsage? ModelUsage { get; init; }
}
