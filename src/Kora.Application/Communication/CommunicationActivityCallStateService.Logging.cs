using Microsoft.Extensions.Logging;

namespace Kora.Application.Communication;

public sealed partial class CommunicationActivityCallStateService
{
    [LoggerMessage(4921, LogLevel.Error, "Communication activity preference is unavailable; automatic evidence remains Unknown.")]
    private static partial void PreferenceFailed(ILogger logger);

    [LoggerMessage(4922, LogLevel.Error, "Communication activity observation or resource release failed; source is quarantined and evidence remains Unknown.")]
    private static partial void ObservationFailed(ILogger logger);
}