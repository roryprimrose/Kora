using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class KnowledgeSourceCaptureLimitsConfigurationService
{
    [LoggerMessage(1, LogLevel.Error, "Source capture limits are unreadable or unconfirmed; refresh remains unavailable.")]
    private partial void LimitsUnavailable(Exception exception);
}
