using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Claude request outcome is unconfirmed ({FailureType}); no replay was attempted.")]
    private static partial void RequestFailed(ILogger logger, string failureType);
}
