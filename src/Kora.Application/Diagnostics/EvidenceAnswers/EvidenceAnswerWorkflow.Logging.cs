using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics.EvidenceAnswers;

public sealed partial class EvidenceAnswerWorkflow
{
    [LoggerMessage(6900, LogLevel.Warning, "Evidence answer unavailable. Failure type: {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
