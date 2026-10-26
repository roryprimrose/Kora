using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

public sealed partial class CustomizationVoiceClarification
{
    [LoggerMessage(1, LogLevel.Warning, "Editor clarification voice operation failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
