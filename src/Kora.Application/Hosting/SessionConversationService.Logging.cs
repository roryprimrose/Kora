using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    [LoggerMessage(4310, LogLevel.Error, "Session conversation operation unconfirmed: {FailureType}; inspect durable request without replay.")]
    private static partial void Failure(ILogger logger, string failureType);

    [LoggerMessage(4311, LogLevel.Information, "Conversation cancellation accepted for task {TaskId}, generation {Generation}; resource join and terminal retention remain pending.")]
    private static partial void CancellationAccepted(ILogger logger, Guid taskId, long generation);
}
