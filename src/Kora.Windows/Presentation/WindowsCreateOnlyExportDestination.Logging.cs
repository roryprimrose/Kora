using Microsoft.Extensions.Logging;

namespace Kora.Windows.Presentation;

internal sealed partial class WindowsCreateOnlyExportDestination
{
    [LoggerMessage(1, LogLevel.Warning, "Evidence page export failed ({ErrorType}); publication occurred: {Published}. No content or path recorded.")]
    internal static partial void ReportFailure(ILogger logger, string errorType, bool published);
}
