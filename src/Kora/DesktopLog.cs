using Microsoft.Extensions.Logging;

namespace Kora;

internal static partial class DesktopLog
{
    [LoggerMessage(300, LogLevel.Debug, "{Operation}.")]
    public static partial void Debug(ILogger logger, string operation);

    [LoggerMessage(301, LogLevel.Information, "{Operation}.")]
    public static partial void Information(ILogger logger, string operation);
}
