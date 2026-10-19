namespace Kora.Core.Context;

/// <summary>Source-use admission, including sensitive-content policy, distinct from local preview consent.</summary>
public interface ILocalFileModelUsePolicy
{
    /// <summary>Checks the exact selected immutable source, question and excerpts before context construction.</summary>
    Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
        LocalFileSearchResult excerpts, CancellationToken cancellationToken);
}
