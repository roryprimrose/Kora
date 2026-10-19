using Kora.Core.Context;

namespace Kora.Application.Dependencies;

/// <summary>No maintained sensitive-content/source-use admission service is currently composed.</summary>
public sealed class UnavailableLocalFileModelUsePolicy : ILocalFileModelUsePolicy
{
    public Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
        LocalFileSearchResult excerpts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LocalFileModelUseDecision.Unavailable);
    }
}
