using Microsoft.Extensions.Logging;

namespace Kora.Application.Timekeeping;

public sealed partial class StopwatchWorkflow
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopwatch time observation failed: {FailureKind}. Run and result retired.")]
    private static partial void TimeObservationFailed(ILogger logger, string failureKind);
}
