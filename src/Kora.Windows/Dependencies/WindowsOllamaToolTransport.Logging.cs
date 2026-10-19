using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaToolTransport
{
    [LoggerMessage(EventId = 4810, Level = LogLevel.Information,
        Message = "Native local tool transport completed with {Outcome}.")]
    private static partial void TransportCompleted(ILogger logger, ModelTurnOutcome outcome);
}
