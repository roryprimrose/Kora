namespace Kora.Core.Context;

/// <summary>Terminal outcomes for one explicitly requested immutable-file answer.</summary>
public enum LocalFileAnswerOutcome
{
    Succeeded, Unsupported, NoEvidence, InvalidQuestion, ContextLimit, Denied, Stale, Busy, Cancelled, Unavailable,
    SourceUseUnavailable, InvalidResponse,
}
