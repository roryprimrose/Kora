using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ClaudeTransmissionWindowController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Claude native disclosure review failed ({FailureType}).")]
    private static partial void DisclosureFailed(ILogger logger, string failureType);
}
