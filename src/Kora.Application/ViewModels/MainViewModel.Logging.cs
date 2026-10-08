using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    [LoggerMessage(115, LogLevel.Error, "Native speech-text presentation failed; exception type {ExceptionType}.")]
    private static partial void CaptionPresentationFailed(ILogger logger, string exceptionType);
}
