using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.KnowledgeSources;

public sealed class KnowledgeSourceAnswerContext
{
    private readonly LocalCitedAnswerContext context;

    public KnowledgeSourceAnswerContext(KnowledgeSourceAnswerReview review, HostRequest request,
        HostRevision generation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(request, review.Target.Request) || generation.Value <= 0 || !review.IsCurrent(now))
        { throw new InvalidDataException("Named evidence requires the exact host-admitted target."); }
        context = new(review.Question, review.Search, request, generation, now, review.ExpiresAt,
            review.IsCurrent, review.Revision.ResolveCitation);
    }

    public ModelContextEnvelope Envelope => context.Envelope;
    public LocalFileAnswerResult Resolve(ModelTurnResult result) => context.Resolve(result);
}
