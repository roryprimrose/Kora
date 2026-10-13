using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SavedDraftCatalogueViewModel
{
    [LoggerMessage(1, LogLevel.Warning, "Saved draft metadata catalogue unavailable: {ExceptionType}")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
