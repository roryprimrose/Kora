using Microsoft.Extensions.Logging;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class SpeechComponentSetupService
{
    [LoggerMessage(1, LogLevel.Warning, "Optional speech component inspection failed")]
    private static partial void InspectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(2, LogLevel.Information, "Optional speech component operation {Operation} completed without runtime selection")]
    private static partial void Completed(ILogger logger, SpeechComponentOperation operation);

    [LoggerMessage(3, LogLevel.Information, "Optional speech component operation {Operation} cancelled; inspection required before retry")]
    private static partial void Cancelled(ILogger logger, SpeechComponentOperation operation);

    [LoggerMessage(4, LogLevel.Error, "Optional speech component operation {Operation} failed; inspection required before retry")]
    private static partial void Failed(ILogger logger, SpeechComponentOperation operation, Exception exception);

    [LoggerMessage(5, LogLevel.Warning, "Optional speech component operation {Operation} denied with mutation state {MutationState}")]
    private static partial void MutationDenied(ILogger logger, SpeechComponentOperation operation, SpeechComponentMutationState mutationState);
}
