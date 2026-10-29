namespace Kora.Core.Context;

/// <summary>Private LocalOnly source-risk admission, distinct from preview consent and non-sensitivity claims.</summary>
public interface ILocalFileModelUsePolicy
{
    /// <summary>Checks the exact selected immutable source, question and excerpts before context construction.</summary>
    Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
        LocalFileSearchResult excerpts, CancellationToken cancellationToken);
}
