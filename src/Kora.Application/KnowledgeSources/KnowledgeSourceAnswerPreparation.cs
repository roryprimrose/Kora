using Kora.Core.Context;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.KnowledgeSources;

public sealed record KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome Outcome, KnowledgeSourceAnswerReview? Review = null);
