using Microsoft.Extensions.Logging;

namespace Kora.Windows.Presentation;

public sealed partial class WindowsExactAnswerExportWriter
{
    [LoggerMessage(1, LogLevel.Warning, "Exact answer export failed ({ErrorType}); publication occurred: {Published}. No content or path recorded.")]
    private partial void Failure(string errorType, bool published);
}
