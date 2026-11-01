namespace Kora.Application.Configuration;

/// <summary>Closed original-user preference operations.</summary>
public enum ConversationExecutionOperation
{
    /// <summary>Inspect confirmed demand without scheduling work.</summary>
    Get,
    /// <summary>Review exact current and proposed demand.</summary>
    Review,
    /// <summary>Confirm the exact current original-channel review.</summary>
    Confirm,
    /// <summary>Discard the inert review.</summary>
    Cancel,
    /// <summary>Reject malformed or unsupported operations without model fallback.</summary>
    Clarify,
}
