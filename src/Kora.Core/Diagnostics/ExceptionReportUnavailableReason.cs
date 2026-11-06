namespace Kora.Core.Diagnostics;

public enum ExceptionReportUnavailableReason
{
    None,
    NoManagedStack,
    LimitExceeded,
    UnsupportedMetadata,
}
