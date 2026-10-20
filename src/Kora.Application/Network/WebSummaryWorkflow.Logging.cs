using Microsoft.Extensions.Logging;

namespace Kora.Application.Network;

public sealed partial class WebSummaryWorkflow
{
    [LoggerMessage(1, LogLevel.Information, "Local web summary cancelled")]
    private partial void Cancelled();

    [LoggerMessage(2, LogLevel.Error, "Local web summary failed: {FailureKind}")]
    private partial void Failed(string failureKind);
}
