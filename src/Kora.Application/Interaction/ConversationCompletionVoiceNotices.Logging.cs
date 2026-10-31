using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

internal sealed partial class ConversationCompletionVoiceNotices
{
    [LoggerMessage(1, LogLevel.Warning, "Completion voice notice held or suppressed: {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}