using Microsoft.Extensions.Logging;

namespace Kora.Application.Maintenance;

public sealed partial class MaintenanceViewModel
{
    [LoggerMessage(321, LogLevel.Error, "Native maintenance operation failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
