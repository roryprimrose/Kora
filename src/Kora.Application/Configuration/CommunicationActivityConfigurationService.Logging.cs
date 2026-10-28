using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class CommunicationActivityConfigurationService
{
    [LoggerMessage(4924, LogLevel.Error, "Communication activity preference operation failed; inspect the original admission and preference evidence before recovery.")]
    private static partial void ConfigurationFailed(ILogger logger);
}