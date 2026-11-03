using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class VoiceCaptureTimingConfigurationService
{
    [LoggerMessage(1, LogLevel.Error, "Capture timing is invalid, unreadable or unconfirmed; new capture is unavailable.")]
    private partial void TimingUnavailable(Exception exception);
}
