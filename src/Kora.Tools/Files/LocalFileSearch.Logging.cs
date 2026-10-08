using Kora.Core.Context;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileSearch
{
    [LoggerMessage(EventId = 744, Level = LogLevel.Information,
        Message = "Selected local revision lexical outcome {SearchOutcome}, citations {CitationCount}, truncated {Truncated}; host-only")]
    private static partial void Report(ILogger logger, LocalFileSearchOutcome searchOutcome, int citationCount, bool truncated);

    [LoggerMessage(EventId = 745, Level = LogLevel.Error,
        Message = "Local lexical retrieval unavailable with {FailureType}; no query or content logged")]
    private static partial void Failure(ILogger logger, string failureType);
}
