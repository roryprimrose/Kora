using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

public sealed record HostQuestionRecord(
    HostQuestionKey Key,
    QuestionSpec Spec,
    HostRevision SessionGeneration,
    DateTimeOffset ExpiresAt,
    QuestionStatus Status = QuestionStatus.Pending,
    QuestionAnswer? Draft = null,
    RequestOrigin? AnswerChannel = null,
    HostOperationProposal? Proposal = null);
