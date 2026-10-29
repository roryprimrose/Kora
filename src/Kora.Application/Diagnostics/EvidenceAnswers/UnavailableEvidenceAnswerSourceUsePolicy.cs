using Kora.Core.Context.EvidenceAnswers;

namespace Kora.Application.Diagnostics.EvidenceAnswers;

/// <summary>Missing source-use policy means no source disclosure or durable model request.</summary>
public sealed class UnavailableEvidenceAnswerSourceUsePolicy : IEvidenceAnswerSourceUsePolicy
{
    public Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceSourceUseDecision.Unavailable);
    }
}
