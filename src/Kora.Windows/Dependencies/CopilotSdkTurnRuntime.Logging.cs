using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class CopilotSdkTurnRuntime
{
    [LoggerMessage(1, LogLevel.Warning, "Copilot late output was discarded after callback authority closed. ExceptionType={ExceptionType}")]
    private static partial void LateOutputDiscarded(ILogger<CopilotSdkTurnRuntime> logger, string exceptionType);

    [LoggerMessage(2, LogLevel.Error, "Copilot process termination is unconfirmed; the runtime remains non-quiescent and unavailable.")]
    private static partial void StopUnconfirmed(ILogger<CopilotSdkTurnRuntime> logger);

    [LoggerMessage(3, LogLevel.Warning, "Copilot final transmission was denied. ExceptionType={ExceptionType}")]
    private static partial void TransmissionDenied(ILogger<CopilotSdkTurnRuntime> logger, string exceptionType);
}
