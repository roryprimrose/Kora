using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    [LoggerMessage(312, LogLevel.Error, "Native evidence inspection failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
