using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class MicrophoneRecoveryWindow
{
    [LoggerMessage(1, LogLevel.Error, "Native microphone recovery failed; no successful recovery is claimed")]
    private static partial void RecoveryFailed(ILogger logger, Exception exception);
}
