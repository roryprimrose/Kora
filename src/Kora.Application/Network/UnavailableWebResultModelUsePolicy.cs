using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.Network;

/// <summary>No production web-source sensitivity/source-use service is currently composed.</summary>
public sealed class UnavailableWebResultModelUsePolicy : IWebResultModelUsePolicy
{
    public Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(WebResultModelUseDecision.Unavailable);
    }
}
