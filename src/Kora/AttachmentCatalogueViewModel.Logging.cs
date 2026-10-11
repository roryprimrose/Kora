using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AttachmentCatalogueViewModel
{
    [LoggerMessage(1, LogLevel.Warning, "Retained attachment catalogue failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
