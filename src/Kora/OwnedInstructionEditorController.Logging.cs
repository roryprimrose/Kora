using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    [LoggerMessage(1, LogLevel.Warning, "Owned instruction editor failed: {FailureType}")]
    private static partial void EditorFailed(ILogger logger, string failureType);
}
