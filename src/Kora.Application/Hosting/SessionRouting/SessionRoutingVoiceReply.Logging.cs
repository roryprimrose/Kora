using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting.SessionRouting;

public sealed partial class SessionRoutingVoiceReply
{
    [LoggerMessage(1, LogLevel.Warning, "Routing reply voice operation failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
