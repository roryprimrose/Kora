using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class QuietHoursConfigurationService
{
    [LoggerMessage(1, LogLevel.Warning, "Quiet-hours preference unavailable during pre-host bootstrap: {FailureType}. Proactive speech is held.")]
    private partial void PreferenceUnavailable(string failureType);
}
