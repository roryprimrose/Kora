using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class ComposerDictationService
{
    [LoggerMessage(1, LogLevel.Warning, "Literal composer dictation failed with {ExceptionType}; no content logged.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
