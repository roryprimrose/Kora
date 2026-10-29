namespace Kora.Core.Context.EvidenceAnswers;

/// <summary>Private LocalOnly source-risk admission, not ordinary inspection consent or non-sensitivity proof.</summary>
public interface IEvidenceAnswerSourceUsePolicy
{
    Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token);
}
