using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class BoundedAudioOutputCatalog
{
    [LoggerMessage(230, LogLevel.Error, "Audio output metadata enumeration failed")]
    private static partial void EnumerationFailed(ILogger logger, Exception exception);
}
