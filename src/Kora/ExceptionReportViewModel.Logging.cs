using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ExceptionReportViewModel
{
    [LoggerMessage(7200, LogLevel.Warning, "GitHub problem-report operation unavailable: {FailureKind}.")]
    private static partial void Failure(ILogger logger, string failureKind);
}
