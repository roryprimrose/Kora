using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ClaudeAccountWindowController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Claude native account operation failed ({FailureType}).")]
    private static partial void SetupFailed(ILogger logger, string failureType);
}
