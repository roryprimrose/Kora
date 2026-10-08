using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class WindowsSpeechRateConfigurationService
{
    [LoggerMessage(EventId = 4901, Level = LogLevel.Error,
        Message = "Windows speech-rate preference or admitted operation was not confirmed")]
    private static partial void PreferenceUnavailable(ILogger logger, Exception exception);
}
