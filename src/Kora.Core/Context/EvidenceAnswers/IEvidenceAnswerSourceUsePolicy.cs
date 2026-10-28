namespace Kora.Core.Context.EvidenceAnswers;

/// <summary>Affirmative source sensitivity/model-use admission, not ordinary inspection consent.</summary>
public interface IEvidenceAnswerSourceUsePolicy
{
    Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token);
}
