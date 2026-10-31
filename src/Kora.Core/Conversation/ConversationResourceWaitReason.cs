namespace Kora.Core.Conversation;

/// <summary>The current factual admission blocker for this exact request.</summary>
public enum ConversationResourceWaitReason
{
    /// <summary>A resource lease was reserved; provider dispatch is still separate.</summary>
    None,
    /// <summary>The desired future execution ceiling is occupied.</summary>
    ConfiguredMaximum,
    /// <summary>An earlier FIFO waiter must be admitted first.</summary>
    EarlierRequest,
    /// <summary>Qualified transports or canonical shared/exclusive resources are occupied.</summary>
    RuntimeResources,
}
