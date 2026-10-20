using Microsoft.Extensions.Logging;

namespace Kora.Application.Clock;

public sealed partial class LocalClockWorkflow
{
    [LoggerMessage(1, LogLevel.Information, "Local clock request {Outcome}")]
    private static partial void Outcome(ILogger logger, LocalClockOutcome outcome);

    [LoggerMessage(2, LogLevel.Error, "Local clock observation failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
