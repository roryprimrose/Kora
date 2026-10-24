namespace Kora.Core.Dependencies;

/// <summary>Content-free reasons for a deliberate local context execution receipt.</summary>
public enum LocalContextTurnReason
{
    Completed,
    OriginalInputRequired,
    TargetAlreadyUsed,
    ContextMismatch,
    SourceNotCurrent,
    SessionNotCurrent,
    Cancelled,
    ModelUnavailable,
    ModelDenied,
    ModelOutcomeUncertain,
    EvidenceUnavailable,
}