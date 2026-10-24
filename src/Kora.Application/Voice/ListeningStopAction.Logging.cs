using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

internal sealed partial class ListeningStopAction
{
    [LoggerMessage(1, LogLevel.Information, "Listening stop completed with {Outcome}.")]
    private static partial void Stopped(ILogger logger, ListeningStopOutcome outcome);

    [LoggerMessage(2, LogLevel.Error, "Listening stop could not confirm native capture release.")]
    private static partial void ReleaseFailed(ILogger logger, Exception exception);
}
