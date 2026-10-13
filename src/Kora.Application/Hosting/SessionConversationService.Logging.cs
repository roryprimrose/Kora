using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    [LoggerMessage(4310, LogLevel.Error, "Session conversation operation unconfirmed: {FailureType}; inspect durable request without replay.")]
    private static partial void Failure(ILogger logger, string failureType);
}
