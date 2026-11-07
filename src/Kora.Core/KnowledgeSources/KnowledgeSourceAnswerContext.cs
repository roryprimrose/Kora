using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.KnowledgeSources;

public sealed class KnowledgeSourceAnswerContext
{
    private readonly LocalCitedAnswerContext context;
    private readonly bool retrievedTruncated;
    private readonly KnowledgeSourceAnswerReview sourceReview;

    public KnowledgeSourceAnswerContext(KnowledgeSourceAnswerReview review, HostRequest request,
        HostRevision generation, DateTimeOffset now)
        : this(review, review?.Target!, request, generation, now)
    {
    }

    public KnowledgeSourceAnswerContext(KnowledgeSourceAnswerReview review, LocalContextSessionTarget target,
        HostRequest request, HostRevision generation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(request, target.Request)
            || !ReferenceEquals(target.SourceRequest, review.Target.SourceRequest)
            || (target.ExpectedGeneration is { } expected
                ? expected != generation : !ReferenceEquals(target, review.Target))
            || generation.Value <= 0 || !review.IsCurrent(now))
        { throw new InvalidDataException("Named evidence requires the exact host-admitted target."); }
        sourceReview = review;
        context = new(review.Question, review.Search, request, generation, now, review.ExpiresAt,
            review.IsCurrent, review.ResolveCitation, review.Sources.Count == 1 ? null
                : citation => System.Text.Json.JsonSerializer.Serialize(new
                {
                    Source = review.DescribeCitation(citation),
                    Excerpt = citation.Excerpt,
                }));
        retrievedTruncated = review.RetrievedSearch.Truncated;
    }

    public ModelContextEnvelope Envelope => context.Envelope;
    public LocalFileAnswerResult Resolve(ModelTurnResult result)
    {
        var resolved = context.Resolve(result);
        return resolved.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported
            ? resolved with { RetrievalTruncated = retrievedTruncated } : resolved;
    }

    public (ModelTurnResult Result, LocalFileAnswerResult Answer) ResolveRetained(ModelTurnResult result)
    {
        var answer = Resolve(result);
        if (result.Outcome != ModelTurnOutcome.Succeeded) { return (result, answer); }
        if (answer.Outcome is not (LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported))
        { return (new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, result.Provenance), answer); }
        var body = new System.Text.StringBuilder(answer.Outcome == LocalFileAnswerOutcome.Unsupported
            ? "The selected evidence does not support an answer."
            : "Model-generated claims; verify the exact retained source citations. Citation identity is checked, not semantic truth.");
        foreach (var claim in answer.Claims)
        {
            body.Append("\n\n").Append(claim.Text);
            foreach (var citation in claim.Citations)
            {
                body.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"\n[{sourceReview.DescribeCitation(citation)}; sha256 {citation.Source.Digest}; UTF-16 {citation.Start}+{citation.Length}; {citation.StartLine}:{citation.StartColumn}-{citation.EndLine}:{citation.EndColumn}]");
            }
        }
        var projected = result with { Response = new(body.ToString(), null) };
        try { _ = ConversationResponse.FromResult(projected, result.Provenance!); }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        {
            return (new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, result.Provenance),
                LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse));
        }
        return (projected, answer);
    }
}
