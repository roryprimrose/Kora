using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class CopilotEvaluationDeploymentSource
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Copilot evaluation unavailable: {FailureType}")]
    private static partial void EvaluationUnavailable(ILogger logger, string failureType);
}
