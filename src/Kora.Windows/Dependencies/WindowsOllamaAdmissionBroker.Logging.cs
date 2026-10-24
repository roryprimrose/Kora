using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaAdmissionBroker
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Local model receipt revoked: {Reason}. Inspect the exact qualification/subject and review admission again.")]
    private static partial void ReceiptUnavailable(ILogger logger, string reason, Exception? exception = null);
}