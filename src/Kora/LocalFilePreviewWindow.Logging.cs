using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    [LoggerMessage(371, LogLevel.Error, "Native local file whitespace inspection failed: {FailureType}. No counts claimed.")]
    private static partial void WhitespaceInspectionFailed(ILogger logger, string failureType);
}
