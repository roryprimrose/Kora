namespace Kora.Core.Context;

/// <summary>Explicit clipboard review and local inference outcomes.</summary>
public enum ClipboardExplanationOutcome
{
    ReviewRequired,
    Succeeded,
    Denied,
    Stale,
    Expired,
    EnvelopeRejected,
    Unavailable,
    Cancelled,
    Busy,
    Failed,
    SourceUsePolicyUnavailable,
    SourceUseDenied,
}