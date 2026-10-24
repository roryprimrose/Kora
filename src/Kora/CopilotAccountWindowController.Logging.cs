using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CopilotAccountWindowController
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Copilot account setup failed with {FailureType}")]
    private static partial void SetupFailed(ILogger logger, string failureType);
}
