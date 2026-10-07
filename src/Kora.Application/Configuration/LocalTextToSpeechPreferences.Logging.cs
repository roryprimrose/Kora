using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class LocalTextToSpeechPreferences
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Saved the coherent speech selection preference")]
    private static partial void SelectionSaved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded the saved {PreferenceName} preference")]
    private static partial void IdentifierLoaded(ILogger logger, string preferenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved the {PreferenceName} preference")]
    private static partial void IdentifierSaved(ILogger logger, string preferenceName);
}
