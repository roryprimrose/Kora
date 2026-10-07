using Kora.Core.Context;

using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardSnapshotBroker
{
    [LoggerMessage(EventId = 740, Level = LogLevel.Information,
        Message = "Explicit local clipboard preview outcome {ClipboardOutcome}; no inference or egress authorized")]
    private static partial void Report(ILogger logger, ClipboardOutcome clipboardOutcome);

    [LoggerMessage(EventId = 741, Level = LogLevel.Error,
        Message = "Local clipboard read failed with {FailureType}; no text retained or success claimed")]
    private static partial void ReadFailed(ILogger logger, string failureType);
}
