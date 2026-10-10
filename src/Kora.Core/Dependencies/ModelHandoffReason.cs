namespace Kora.Core.Dependencies;

/// <summary>Observable host conditions. Model confidence is deliberately not a condition.</summary>
public enum ModelHandoffReason
{
    Unknown,
    LocalUnavailable,
    ContextBudgetExceeded,
    StructuredOutputRejected,
    AcceptanceCheckFailed,
    HostedCapabilityRequired,
    UserRequestedHosted,
    UserRequestedSecondOpinion,
}
