using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class QuestionView
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Native question activation failed ({FailureType})")]
    private partial void ActivationFailed(string failureType);
}
