using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.Network;

/// <summary>Fail-closed fallback for a host without a maintained web source-use policy.</summary>
public sealed class UnavailableWebResultModelUsePolicy : IWebResultModelUsePolicy
{
    public Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(WebResultModelUseDecision.Unavailable);
    }
}
