using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class InstructionUseController
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "One-shot instruction operation failed with {FailureKind}")]
    private static partial void Failed(ILogger logger, string failureKind);
}
