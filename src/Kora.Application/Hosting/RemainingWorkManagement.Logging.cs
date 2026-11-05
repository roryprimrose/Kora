using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class RemainingWorkManagement
{
    [LoggerMessage(1, LogLevel.Warning, "Remaining-work management answer failed: {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}