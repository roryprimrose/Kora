using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class SessionProviderProfileConfigurationService
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Exact session provider configuration is unconfirmed: {ExceptionType}")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
