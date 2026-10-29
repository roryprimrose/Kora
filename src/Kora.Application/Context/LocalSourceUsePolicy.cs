using System.Text;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Core.Context;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.Context;

/// <summary>Shared host risk policy; callers retain independent consent, live authority and resource ownership.</summary>
public sealed class LocalSourceUsePolicy : ILocalFileModelUsePolicy, IWebResultModelUsePolicy,
    IEvidenceAnswerSourceUsePolicy, IMcpResultSourceUse
{
    public static LocalSourceUsePolicy Instance { get; } = new();

    public Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
        LocalFileSearchResult excerpts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(excerpts);
        cancellationToken.ThrowIfCancellationRequested();
        if (!LocalFileRetrievalPolicy.TryQuery(question, out _) || excerpts.Outcome != LocalFileSearchOutcome.Matched
            || excerpts.Mode != LocalFileRetrievalMode.LexicalLines
            || !string.Equals(excerpts.PolicyVersion, LocalFileRetrievalPolicy.Version, StringComparison.Ordinal)
            || excerpts.Citations.Count is < 1 or > LocalFileRetrievalPolicy.MaximumCitations
            || excerpts.MatchingChunks < excerpts.Citations.Count)
        { return Task.FromResult(LocalFileModelUseDecision.Denied); }
        var bytes = 0;
        var spans = new HashSet<(int Start, int Length)>();
        foreach (var citation in excerpts.Citations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { _ = LocalFileLexicalRetrieval.ResolveCitation(source, citation, excerpts.Mode); }
            catch (InvalidDataException) { return Task.FromResult(LocalFileModelUseDecision.Denied); }
            bytes += Encoding.UTF8.GetByteCount(citation.Excerpt);
            if (bytes > LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes || !spans.Add((citation.Start, citation.Length)))
            { return Task.FromResult(LocalFileModelUseDecision.Denied); }
        }
        var risk = LocalSourceRiskPolicy.Evaluate([source.Text, question, .. excerpts.Citations.Select(item => item.Excerpt)],
            cancellationToken);
        return Task.FromResult(risk switch
        {
            LocalSourceRiskOutcome.AdmittedPrivateLocalOnly => LocalFileModelUseDecision.Allowed,
            LocalSourceRiskOutcome.DetectedRisk => LocalFileModelUseDecision.Denied,
            _ => LocalFileModelUseDecision.Unavailable,
        });
    }

    public Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var risk = LocalSourceRiskPolicy.Evaluate([snapshot.Source, snapshot.Text,
            snapshot.Provenance.RequestedAddress, snapshot.Provenance.FinalAddress], token);
        return Task.FromResult(risk switch
        {
            LocalSourceRiskOutcome.AdmittedPrivateLocalOnly => WebResultModelUseDecision.Allowed,
            LocalSourceRiskOutcome.DetectedRisk => WebResultModelUseDecision.Denied,
            _ => WebResultModelUseDecision.Unavailable,
        });
    }

    public Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        return Task.FromResult(LocalSourceRiskPolicy.Evaluate([review.Page, review.Question], token) switch
        {
            LocalSourceRiskOutcome.AdmittedPrivateLocalOnly => EvidenceSourceUseDecision.Allowed,
            LocalSourceRiskOutcome.DetectedRisk => EvidenceSourceUseDecision.Denied,
            _ => EvidenceSourceUseDecision.Unavailable,
        });
    }

    McpResultSourceUseDecision IMcpResultSourceUse.Check(McpResultObservation observation, string question,
        RequestOrigin questionOrigin, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(question);
        token.ThrowIfCancellationRequested();
        if (observation.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || questionOrigin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { return new(McpResultSourceUseKind.Denied, LocalSourceRiskPolicy.Revision); }
        var risk = LocalSourceRiskPolicy.Evaluate([question, observation.Result.Text, observation.Result.StructuredJson ?? string.Empty,
            observation.Provenance.Server, observation.Provenance.Endpoint, observation.Provenance.Account,
            observation.Provenance.Tool, observation.Mapping.InputSchema!], token);
        return new(risk switch
        {
            LocalSourceRiskOutcome.AdmittedPrivateLocalOnly => McpResultSourceUseKind.QualifiedLocalOnly,
            LocalSourceRiskOutcome.DetectedRisk => McpResultSourceUseKind.Denied,
            _ => McpResultSourceUseKind.Unavailable,
        }, LocalSourceRiskPolicy.Revision);
    }
}
