using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SharedSkillSourcesWindowController
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared profile skill inspection unavailable: {ExceptionType}")]
    private static partial void InspectionFailed(ILogger logger, string exceptionType);
}
