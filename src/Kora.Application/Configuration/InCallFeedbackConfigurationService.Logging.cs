using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class InCallFeedbackConfigurationService
{
    [LoggerMessage(EventId = 4911, Level = LogLevel.Error,
        Message = "In-call feedback preference or admitted operation was not confirmed")]
    private static partial void PreferenceUnavailable(ILogger logger, Exception exception);
}
