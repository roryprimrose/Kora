namespace Kora.Core.Network;

public enum WebUrlSyntaxOutcome
{
    Accepted,
    OutsideBounds,
    InvalidSyntax,
    Stale,
    Cancelled,
    Busy,
    Failed,
}
