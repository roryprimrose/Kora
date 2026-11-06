using Microsoft.Extensions.Logging;

namespace Kora;

internal static partial class ExactAnswerExportDiagnostics
{
    [LoggerMessage(1, LogLevel.Warning, "Native exact answer export failed ({ErrorType}); no content or path recorded.")]
    internal static partial void Report(ILogger logger, string errorType);
}
