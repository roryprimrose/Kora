using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

internal sealed partial class OllamaAdmissionWorkflow
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Local model admission unavailable: {Reason}.")]
    private static partial void Unavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Local model admission held unavailable after {FailureType}; explicit review recovery required.")]
    private static partial void Failed(ILogger logger, string failureType);
}