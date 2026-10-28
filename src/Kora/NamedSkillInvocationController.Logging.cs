using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NamedSkillInvocationController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Named skill operation unavailable or unconfirmed ({ExceptionType}).")]
    private static partial void UseFailed(ILogger logger, string exceptionType);
}
