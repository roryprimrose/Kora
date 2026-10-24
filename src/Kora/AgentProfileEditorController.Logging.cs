using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentProfileEditorController
{
    [LoggerMessage(EventId = 7470, Level = LogLevel.Warning, Message = "Owned agent authoring failed ({FailureType}).")]
    private static partial void AuthoringFailed(ILogger logger, string failureType);
}
