using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    [LoggerMessage(313, LogLevel.Error, "Native Sessions workspace failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
