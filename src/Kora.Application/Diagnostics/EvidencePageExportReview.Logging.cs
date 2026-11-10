using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed partial class EvidencePageExportReview
{
    [LoggerMessage(1, LogLevel.Warning, "Evidence page export failed ({ErrorType}); no content or destination recorded.")]
    private partial void Failure(string errorType);
}
