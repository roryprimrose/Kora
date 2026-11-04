using Microsoft.Extensions.Logging;

namespace Kora.Application.KnowledgeSources;

public sealed partial class KnowledgeSourceAnswerWorkflow
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Named source answer unavailable. Failure type: {FailureType}")]
    private partial void Failure(string failureType);
}
