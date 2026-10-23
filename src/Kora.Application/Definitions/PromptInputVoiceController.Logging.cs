using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

public sealed partial class PromptInputVoiceController
{
    [LoggerMessage(1, LogLevel.Warning, "Prompt input voice operation failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
