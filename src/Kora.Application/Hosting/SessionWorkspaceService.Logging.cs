using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    [LoggerMessage(182, LogLevel.Error, "Sessions workspace failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
