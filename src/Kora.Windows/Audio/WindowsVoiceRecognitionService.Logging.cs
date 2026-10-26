using Microsoft.Extensions.Logging;

namespace Kora.Windows.Audio;

public sealed partial class WindowsVoiceRecognitionService
{
    [LoggerMessage(1, LogLevel.Warning, "Provisional recognition observer retired with {ExceptionType}; no content logged.")]
    private static partial void ProvisionalObserverFailed(ILogger logger, string exceptionType);
}
