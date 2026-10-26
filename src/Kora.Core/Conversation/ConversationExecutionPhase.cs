namespace Kora.Core.Conversation;

/// <summary>Separates an accepted cancellation from resource termination and confirmed terminal storage.</summary>
public enum ConversationExecutionPhase
{
    /// <summary>The original retained request is waiting for host-owned resources, not executing.</summary>
    Waiting = -1,
    /// <summary>The exact admitted turn can still accept cancellation.</summary>
    Running,
    /// <summary>Cancellation was accepted; owned work is still being joined.</summary>
    Cancelling,
    /// <summary>Terminal storage owns the race; cancellation cannot roll it back.</summary>
    Completing,
    /// <summary>Resource join and exact terminal storage/readback completed.</summary>
    Terminal,
    /// <summary>Terminal retention or audit could not be confirmed; inspect without replay.</summary>
    Unconfirmed,
}