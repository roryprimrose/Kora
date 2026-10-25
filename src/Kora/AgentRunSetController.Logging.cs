using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentRunSetController
{
    [LoggerMessage(5920, LogLevel.Warning, "Independent agent operation failed: {ExceptionType}")]
    private static partial void Failed(ILogger logger, string exceptionType);
}
