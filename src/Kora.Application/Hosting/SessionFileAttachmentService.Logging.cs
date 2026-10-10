using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionFileAttachmentService
{
    [LoggerMessage(1, LogLevel.Warning, "Session attachment operation failed. FailureType={FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);

    [LoggerMessage(2, LogLevel.Information, "Session attachment inspection cancelled; no late content published.")]
    private static partial void Cancelled(ILogger logger);
}
