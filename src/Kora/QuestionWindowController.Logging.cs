using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    [LoggerMessage(311, LogLevel.Error, "Native version review failed; exception type {ExceptionType}.")]
    private static partial void QueryFailure(ILogger logger, string? exceptionType);
}
