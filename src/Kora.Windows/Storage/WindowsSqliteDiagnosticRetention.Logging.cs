using Microsoft.Extensions.Logging;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteDiagnosticRetention
{
    [LoggerMessage(208, LogLevel.Information,
        "Committed ordinary diagnostic retention: {LogCount} logs, {SpanCount} spans, {LinkCount} links; due backlog {HasMore}.")]
    private static partial void Pruned(ILogger logger, int logCount, int spanCount, int linkCount, bool hasMore);
}
