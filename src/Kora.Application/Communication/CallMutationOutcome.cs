namespace Kora.Application.Communication;

public enum CallMutationOutcome
{
    Applied,
    Unchanged,
    HostUnavailable,
    StaleObservation,
    OriginDenied,
    ExactReviewUnavailable,
    PersistenceFailed,
}
