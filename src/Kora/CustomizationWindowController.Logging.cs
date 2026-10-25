using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Customization operation failed with {FailureType}")]
    private static partial void CustomizationFailed(ILogger logger, string failureType);
}
