using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
    [LoggerMessage(1, LogLevel.Information, "Hands-free voice state changed to {State}.")]
    private static partial void LifecycleStateChanged(ILogger logger, HandsFreeVoiceState state);

    [LoggerMessage(2, LogLevel.Error, "Hands-free voice failed with {FailureKind}; explicit recovery required.")]
    private static partial void LifecycleFailed(ILogger logger, string? failureKind);
}
