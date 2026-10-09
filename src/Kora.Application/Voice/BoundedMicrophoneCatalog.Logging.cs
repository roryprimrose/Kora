using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class BoundedMicrophoneCatalog
{
    [LoggerMessage(3100, LogLevel.Error, "Microphone metadata enumeration failed; late results confer no recovery authority")]
    private static partial void EnumerationFailed(ILogger logger, Exception exception);
}
