using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class MarkdownDisplayConfigurationService
{
    [LoggerMessage(1, LogLevel.Error, "Markdown display preference unavailable or unconfirmed; no saved default was applied.")]
    private partial void PreferenceUnavailable(Exception exception);
}
