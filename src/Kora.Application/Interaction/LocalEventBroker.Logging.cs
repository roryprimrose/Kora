using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class LocalEventBroker
{
    [LoggerMessage(EventId = 8430, Level = LogLevel.Debug,
        Message = "Bounded native local events observed: {Count}; omitted: {Omitted}.")]
    private static partial void Observed(ILogger logger, int count, int omitted);

    [LoggerMessage(EventId = 8431, Level = LogLevel.Warning,
        Message = "Local event broker unavailable: {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 8432, Level = LogLevel.Information,
        Message = "Run-only routine notice quiet choice changed: {Enabled}; revision: {Revision}.")]
    private static partial void QuietChanged(ILogger logger, bool enabled, long revision);
}
