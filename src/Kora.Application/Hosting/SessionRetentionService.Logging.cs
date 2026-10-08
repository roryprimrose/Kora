using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionRetentionService
{
    [LoggerMessage(1, LogLevel.Information, "Session retention committed: {Archived} archived, {Deleted} deleted, {Held} held; backlog {HasMore}.")]
    private static partial void Completed(ILogger logger, int archived, int deleted, int held, bool hasMore);

    [LoggerMessage(2, LogLevel.Error, "Session retention failed; failure kind {FailureKind}.")]
    private static partial void Failure(ILogger logger, string failureKind);

    [LoggerMessage(3, LogLevel.Debug, "Session retention is held by unavailable configuration or host admission.")]
    private static partial void Held(ILogger logger);
}
