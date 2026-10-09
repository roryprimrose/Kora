using Kora.Core.Context;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFilePreview
{
    [LoggerMessage(EventId = 742, Level = LogLevel.Information,
        Message = "Explicit local file preview outcome {FileOutcome}; no model or egress authority")]
    private static partial void Report(ILogger logger, LocalFileOutcome fileOutcome);

    [LoggerMessage(EventId = 743, Level = LogLevel.Error,
        Message = "Local file inspection failed with {FailureType}; no path or content logged")]
    private static partial void Failure(ILogger logger, string failureType);
}
