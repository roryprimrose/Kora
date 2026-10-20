namespace Kora.Core.Context;

/// <summary>Trusted source-policy decision for one exact immutable clipboard snapshot.</summary>
public enum ClipboardSourceUseOutcome
{
    Unavailable,
    Denied,
    Accepted,
}
