using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class HostQuestionVoiceReplyService
{
    [LoggerMessage(1, LogLevel.Error, "Question voice reply failed with {FailureType}; no content logged")]
    private static partial void ReplyFailure(ILogger logger, string? failureType);
}
