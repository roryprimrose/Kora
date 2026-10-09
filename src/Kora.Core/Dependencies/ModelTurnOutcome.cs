namespace Kora.Core.Dependencies;

public enum ModelTurnOutcome
{
    Unknown,
    Succeeded,
    Unavailable,
    AuthenticationRequired,
    QuotaExceeded,
    TimedOut,
    Cancelled,
    DeniedEgress,
    Denied,
}
