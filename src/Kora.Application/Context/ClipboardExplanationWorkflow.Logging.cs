using Kora.Core.Context;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Context;

public sealed partial class ClipboardExplanationWorkflow
{
    [LoggerMessage(1, LogLevel.Information, "Clipboard explanation {Outcome}; capture {CaptureOutcome}; model reason {ModelReason}")]
    private static partial void Outcome(ILogger logger, ClipboardExplanationOutcome outcome, ClipboardOutcome? captureOutcome, ModelTurnReason? modelReason);

    [LoggerMessage(2, LogLevel.Error, "Clipboard explanation failed with {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}