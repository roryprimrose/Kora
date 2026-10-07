using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class AppearanceConfigurationService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected stale appearance proposal for {OptionId} at revision {Revision}")]
    private static partial void StaleProposal(ILogger logger, string optionId, long revision);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving appearance option {OptionId} failed")]
    private static partial void SaveFailed(ILogger logger, string optionId, Exception exception);
}
