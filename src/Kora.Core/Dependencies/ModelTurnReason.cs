namespace Kora.Core.Dependencies;

public enum ModelTurnReason
{
    None,
    HostContextRequired,
    HostAdmissionClosed,
    SessionNotAdmitted,
    ContextMissing,
    ContextExpired,
    ContextMismatch,
    EnvelopeLimitExceeded,
    UnregisteredModel,
    QualificationPending,
    EgressNotAdmitted,
    TurnAlreadyUsed,
    CallerCancelled,
    DeadlineExceeded,
    ProviderOutcome,
    InvalidProviderResponse,
    ProviderBusy,
}
