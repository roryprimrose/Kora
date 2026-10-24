using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentRunController
{
    [LoggerMessage(1, LogLevel.Warning, "Bounded agent operation unavailable or unconfirmed: {FailureKind}")]
    private static partial void Failed(ILogger logger, string failureKind);
}
