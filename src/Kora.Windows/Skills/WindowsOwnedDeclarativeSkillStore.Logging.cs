using Microsoft.Extensions.Logging;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore
{
    [LoggerMessage(7840, LogLevel.Warning,
        "Owned skill directory publication was refused (Windows error {WindowsError}) and nothing was published; attempt {NextAttempt} of {MaximumAttempts} follows after {DelayMilliseconds} ms. No content or path recorded.")]
    private static partial void PublicationRetrying(ILogger logger, int windowsError, int nextAttempt, int maximumAttempts,
        int delayMilliseconds);

    [LoggerMessage(7841, LogLevel.Warning,
        "Unpublished private skill staging cleanup failed ({CleanupErrorType}); the original failure ({OriginalErrorType}) was preserved and the stage is left for verified reaping. No content or path recorded.")]
    private static partial void StagingCleanupFailed(ILogger logger, string cleanupErrorType, string originalErrorType);

    [LoggerMessage(7842, LogLevel.Information,
        "Reaped one orphaned private skill staging directory ({EntryCount} verified entries). No content or path recorded.")]
    private static partial void OrphanStagingReaped(ILogger logger, int entryCount);

    [LoggerMessage(7843, LogLevel.Warning,
        "Orphaned private skill staging was retained ({ErrorType}); nothing outside verified staging was deleted. No content or path recorded.")]
    private static partial void OrphanStagingRetained(ILogger logger, string errorType);
}
