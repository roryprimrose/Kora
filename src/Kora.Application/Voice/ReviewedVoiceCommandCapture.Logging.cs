using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class ReviewedVoiceCommandCapture
{
    [LoggerMessage(1, LogLevel.Warning, "Reviewed command capture failed: {FailureType}")]
    private static partial void Failed(ILogger logger, string failureType);
}
