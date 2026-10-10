using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    [LoggerMessage(377, LogLevel.Error, "Passive work overview failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
