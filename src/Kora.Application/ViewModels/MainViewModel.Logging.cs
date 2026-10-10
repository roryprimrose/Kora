using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    [LoggerMessage(118, LogLevel.Warning, "Original-user web-result presentation failed; exception type {ExceptionType}.")]
    private static partial void WebPagePresentationFailed(ILogger logger, string exceptionType);

    [LoggerMessage(115, LogLevel.Error, "Native speech-text presentation failed; exception type {ExceptionType}.")]
    private static partial void CaptionPresentationFailed(ILogger logger, string exceptionType);

    [LoggerMessage(116, LogLevel.Warning, "Exact local event command unavailable; exception type {ExceptionType}.")]
    private static partial void LocalEventCommandFailed(ILogger logger, string exceptionType);

    [LoggerMessage(117, LogLevel.Warning, "Original-user memory command unavailable; exception type {ExceptionType}.")]
    private static partial void MemoryCommandFailed(ILogger logger, string exceptionType);
}
