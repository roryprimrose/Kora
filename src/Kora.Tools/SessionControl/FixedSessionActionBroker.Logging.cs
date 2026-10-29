using Microsoft.Extensions.Logging;

namespace Kora.Tools.SessionControl;

public sealed partial class FixedSessionActionBroker
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Fixed Windows request threw; native effect is unconfirmed and must not be retried")]
    private static partial void NativeRequestUnconfirmed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fixed Windows request terminal audit is unconfirmed; do not retry")]
    private static partial void TerminalAuditUnconfirmed(ILogger logger, Exception exception);
}