using Microsoft.Extensions.Logging;

namespace Kora.Windows.Communication;

public sealed partial class WindowsCommunicationsActivitySource
{
    internal sealed partial class WindowsCommunicationsObserver
    {
        [LoggerMessage(4923, LogLevel.Error, "Windows communications notification release failed; the retired subscription remains owned in quarantine.")]
        private static partial void ReleaseFailed(ILogger logger);
    }
}