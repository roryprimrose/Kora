using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Claude request outcome is unconfirmed ({FailureType}); no replay was attempted.")]
    private static partial void RequestFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Claude account lease renewal failed ({FailureType}); the lease lapses and later checks fail closed.")]
    private static partial void LeaseRenewalFailed(ILogger logger, string failureType);
}
