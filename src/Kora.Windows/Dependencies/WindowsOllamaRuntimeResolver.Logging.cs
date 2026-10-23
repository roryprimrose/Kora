using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaRuntimeResolver
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Local model runtime unavailable: {Reason}. Inspect exact-profile qualification and process ownership; no fallback.")]
    private static partial void Unavailable(ILogger logger, string reason);
}
