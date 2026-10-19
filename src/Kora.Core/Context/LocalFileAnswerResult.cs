using System.Collections.Immutable;
using Kora.Core.Dependencies;

namespace Kora.Core.Context;

public sealed record LocalFileAnswerResult(LocalFileAnswerOutcome Outcome, ImmutableArray<LocalFileAnswerClaim> Claims,
    int MatchingChunks = 0, bool RetrievalTruncated = false, ModelTurnReason? ModelReason = null)
{
    public static LocalFileAnswerResult Empty(LocalFileAnswerOutcome outcome) => new(outcome, []);
}
