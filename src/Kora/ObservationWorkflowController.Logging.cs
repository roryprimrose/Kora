using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ObservationWorkflowController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Observation workflow unavailable: {FailureType}.")]
    private static partial void WorkflowFailed(ILogger logger, string failureType);
}
