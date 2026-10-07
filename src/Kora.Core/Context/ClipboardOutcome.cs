namespace Kora.Core.Context;

public enum ClipboardOutcome
{
    Captured,
    Reused,
    Revoked,
    Empty,
    UnsupportedFormat,
    Oversize,
    InvalidText,
    Busy,
    AccessDenied,
    Changed,
    Unavailable,
    Denied,
    Stale,
    Cancelled,
}
