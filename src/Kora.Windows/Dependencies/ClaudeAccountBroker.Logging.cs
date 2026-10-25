using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeAccountBroker
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Claude account setup failed ({FailureType}).")]
    private static partial void SetupFailed(ILogger logger, string failureType);
}
