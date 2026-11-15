using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaEvaluationSource
{
    private static void EvaluationFailed(ILogger logger, Exception exception)
    {
        if (!logger.IsEnabled(LogLevel.Information)) { return; }
        var reason = exception.GetType().Name;
        EvaluationUnavailable(logger, reason);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Local model evaluation unavailable: {Reason}. Review the exact supported running runtime and pinned model again; no substitute is authorized.")]
    private static partial void EvaluationUnavailable(ILogger logger, string reason);
}
