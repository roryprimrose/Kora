using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CopilotTransmissionWindowController
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "Copilot disclosure presentation failed with {FailureType}")]
    private static partial void DisclosureFailed(ILogger logger, string failureType);
}
