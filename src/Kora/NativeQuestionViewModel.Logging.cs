using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NativeQuestionViewModel
{
    [LoggerMessage(310, LogLevel.Error,
        "Native question failed for {QuestionId} revision {QuestionRevision}; exception type {ExceptionType}.")]
    private static partial void QuestionFailure(ILogger logger, Guid questionId, long questionRevision, string? exceptionType);
}
