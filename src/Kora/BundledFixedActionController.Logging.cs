using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class BundledFixedActionController
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Governed bundled action or required terminal evidence failed")]
    private static partial void Failed(ILogger logger, Exception exception);
}