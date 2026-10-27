using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CustomizationDiscoveryController
{
    [LoggerMessage(1, LogLevel.Warning, "Customization metadata discovery was not confirmed ({FailureType})")]
    private static partial void Failure(ILogger logger, string failureType);
}
