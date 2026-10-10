using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ModelHandoffWindowController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Exact native handoff review failed with {ExceptionType}; no approval or transmission is claimed")]
    private static partial void Failure(ILogger<ModelHandoffWindowController> logger, string exceptionType);
}
