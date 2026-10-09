using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class AssistantNameConfigurationService
{
    [LoggerMessage(4600, LogLevel.Error, "Assistant name preference read failed")]
    private static partial void ReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(4601, LogLevel.Warning, "Assistant name configuration rejected: {Reason}")]
    private static partial void Rejected(ILogger logger, string reason);

    [LoggerMessage(4602, LogLevel.Error, "Assistant name configuration failed: {Reason}")]
    private static partial void MutationFailed(ILogger logger, string reason, Exception exception);

    [LoggerMessage(4603, LogLevel.Error, "Assistant name preference committed without confirmed audit/application completion; routing held unavailable")]
    private static partial void CompletionUnconfirmed(ILogger logger);
}
