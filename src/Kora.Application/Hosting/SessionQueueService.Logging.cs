using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    [LoggerMessage(184, LogLevel.Error, "Deterministic session queue failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
