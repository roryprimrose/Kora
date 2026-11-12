using Microsoft.Extensions.Logging;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteSessionRetentionPlannerReader
{
    [LoggerMessage(209, LogLevel.Warning,
        "Retention planner observation rejected at {Stage}: {FailureType}; observed revision {ObservedRevision}, current revision {CurrentRevision}, disposed {Disposed}, source lost {SourceLost}.")]
    private static partial void ObservationRejected(ILogger logger, string stage, string failureType,
        long observedRevision, long currentRevision, bool disposed, bool sourceLost);
}
