using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class BoundedAudioOutputCatalog
{
    [LoggerMessage(230, LogLevel.Error, "Audio output metadata enumeration failed ({FailureKind})")]
    private static partial void EnumerationFailed(ILogger logger, string failureKind);
}
