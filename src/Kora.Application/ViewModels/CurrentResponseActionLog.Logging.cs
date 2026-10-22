using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

internal static partial class CurrentResponseActionLog
{
    [LoggerMessage(120, LogLevel.Warning, "Exact current response action failed; exception type {ExceptionType}.")]
    internal static partial void CurrentResponseActionFailed(ILogger logger, string exceptionType);
}
