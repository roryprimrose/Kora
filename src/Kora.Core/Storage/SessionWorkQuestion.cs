using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

public sealed record SessionWorkQuestion(HostQuestionKey Key, HostRevision Generation,
    QuestionStatus Status, QuestionKind Kind, RequestOrigin? AnswerChannel, DateTimeOffset ExpiresAt);
