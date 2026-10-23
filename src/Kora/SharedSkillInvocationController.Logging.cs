using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SharedSkillInvocationController
{
    [LoggerMessage(1, LogLevel.Warning, "Reviewed shared skill use failed: {FailureType}")]
    private static partial void UseFailed(ILogger logger, string failureType);
}
