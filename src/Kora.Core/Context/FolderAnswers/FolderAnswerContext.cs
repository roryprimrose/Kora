using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Context.FolderAnswers;

public sealed class FolderAnswerContext
{
    private readonly LocalCitedAnswerContext context;

    public FolderAnswerContext(FolderAnswerReview review, HostRequest request, HostRevision generation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(request, review.Target.Request) || generation.Value <= 0
            || !review.IsUserSelected || !review.IsCurrent(now))
        { throw new InvalidDataException("Folder evidence requires the exact host-admitted target."); }
        retrievedTruncated = review.RetrievedSearch.Truncated;
        context = new(review.Question, review.Search, request, generation, now, review.ExpiresAt,
            review.IsCurrent, citation => LocalFileLexicalRetrieval.ResolveCitation(
                review.Source.Files.Single(file => file.Reference == citation.Source), citation, LocalFileRetrievalMode.LexicalLines));
    }

    public ModelContextEnvelope Envelope => context.Envelope;
    public LocalFileAnswerResult Resolve(ModelTurnResult result)
    {
        var resultValue = context.Resolve(result);
        return resultValue.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported
            ? resultValue with { RetrievalTruncated = retrievedTruncated } : resultValue;
    }

    private readonly bool retrievedTruncated;
}
