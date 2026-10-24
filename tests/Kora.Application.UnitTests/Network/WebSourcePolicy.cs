using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Network;

internal sealed class WebSourcePolicy : IWebResultModelUsePolicy
{
    internal WebResultModelUseDecision Decision { get; set; } = WebResultModelUseDecision.Allowed;
    internal int Calls { get; private set; }
    internal WebResultSnapshot? Source { get; private set; }
    internal Action? BeforeDecision { get; set; }

    public Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Calls++;
        Source = snapshot;
        BeforeDecision?.Invoke();
        return Task.FromResult(Decision);
    }
}
