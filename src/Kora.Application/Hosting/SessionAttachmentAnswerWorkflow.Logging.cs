using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionAttachmentAnswerWorkflow
{
    [LoggerMessage(1, LogLevel.Warning, "Attachment answer review failed with {FailureType}. No source content is logged.")]
    private static partial void Failure(ILogger logger, string failureType);
}
