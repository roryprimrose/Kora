using Kora.Core.Context;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileAnswer
{
    [LoggerMessage(1, LogLevel.Information, "Local file answer outcome {AnswerOutcome}; claim count {ClaimCount}")]
    private static partial void Report(ILogger logger, LocalFileAnswerOutcome answerOutcome, int claimCount);

    [LoggerMessage(2, LogLevel.Warning, "Local file answer unavailable; failure type {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
