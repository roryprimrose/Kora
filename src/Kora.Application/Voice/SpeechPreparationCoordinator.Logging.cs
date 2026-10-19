using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class SpeechPreparationCoordinator
{
    [LoggerMessage(210, LogLevel.Error, "Preparing the selected local speech voice failed.")]
    private partial void PreparationFailed(Exception exception);
}
