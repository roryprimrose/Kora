using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Exact grant management unavailable: {FailureType}")]
    private partial void Failed(string failureType);
}
