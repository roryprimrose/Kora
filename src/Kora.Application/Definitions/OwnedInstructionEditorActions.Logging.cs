using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    [LoggerMessage(1, LogLevel.Warning, "Owned draft structural validation rejected: {ReasonCode}")]
    private static partial void DraftRejected(ILogger logger, string reasonCode);
}
