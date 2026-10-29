using Kora.Core.Context;

namespace Kora.Application.Dependencies;

/// <summary>Fail-closed fallback for a host without a maintained source-use policy.</summary>
public sealed class UnavailableLocalFileModelUsePolicy : ILocalFileModelUsePolicy
{
    public Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
        LocalFileSearchResult excerpts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LocalFileModelUseDecision.SourceUsePolicyMissing);
    }
}
