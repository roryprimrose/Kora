using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class SpeechConfigurationService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Ordinary summary speech refused: {Reason}")]
    private static partial void SummarySpeechRefused(ILogger logger, string reason);
    [LoggerMessage(Level = LogLevel.Error, Message = "Spoken summary limit preferences could not be read")]
    private static partial void SummaryLimitsReadFailed(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid saved speech selection")]
    private static partial void InvalidSavedSelection(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Speech option {OptionId} rejected: {Reason}")]
    private static partial void MutationRejected(ILogger logger, string optionId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Speech option {OptionId} could not be saved")]
    private static partial void SaveFailed(ILogger logger, string optionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Speech selection preferences could not be read")]
    private static partial void SelectionReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Installed speech catalogue could not be read")]
    private static partial void CatalogueReadFailed(ILogger logger, Exception exception);
}
