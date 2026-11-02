using Microsoft.Extensions.Logging;

namespace Kora.Application.Presentation;

public sealed partial class ExactAnswerExportReview
{
    [LoggerMessage(1, LogLevel.Warning, "Exact answer export failed ({ErrorType}); no content or destination recorded.")]
    private partial void Failure(string errorType);
}
