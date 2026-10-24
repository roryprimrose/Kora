using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedSkillEditorController
{
    [LoggerMessage(4401, LogLevel.Warning, "Owned skill authoring failed with {FailureType}")]
    private static partial void AuthoringFailed(ILogger logger, string failureType);
}
