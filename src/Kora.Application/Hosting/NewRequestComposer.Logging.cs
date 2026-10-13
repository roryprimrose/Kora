using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    [LoggerMessage(1, LogLevel.Warning, "Native New request failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
