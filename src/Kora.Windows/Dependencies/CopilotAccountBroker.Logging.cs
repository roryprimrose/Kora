using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class CopilotAccountBroker
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Copilot account operation ended with {FailureType}; late setup output discarded")]
    private static partial void SetupOutputDiscarded(ILogger logger, string failureType);
}
