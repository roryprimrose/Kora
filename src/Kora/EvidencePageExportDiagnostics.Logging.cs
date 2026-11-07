using Kora.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kora;

internal static partial class EvidencePageExportDiagnostics
{
    [LoggerMessage(1, LogLevel.Warning, "Native evidence export failed ({ErrorType}); no content or destination recorded.")]
    internal static partial void Report(ILogger<EvidencePageExportReview> logger, string errorType);
}
