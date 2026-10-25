using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedSkillInvocationController
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Owned skill use failed ({FailureType}).")]
    private static partial void UseFailed(ILogger logger, string failureType);
}
