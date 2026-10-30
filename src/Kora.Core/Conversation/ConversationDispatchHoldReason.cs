namespace Kora.Core.Conversation;

/// <summary>Current-run manual dispatch inhibition, independent of recovery and authority fences.</summary>
public enum ConversationDispatchHoldReason
{
    /// <summary>No deliberate manual hold.</summary>
    None,

    /// <summary>The original user explicitly held future ordinary dispatch.</summary>
    OriginalUser,
}
