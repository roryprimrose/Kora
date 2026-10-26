using System.Runtime.CompilerServices;

using Kora.Application.Tools;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, CustomizationDiscoverySnapshot> customizationDiscoveries = new();

    internal void BindCustomizationDiscovery(ModelTurn turn, CustomizationDiscoverySnapshot snapshot)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.CustomizationDiscovery
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || turn.Provenance.Request.SessionId != snapshot.Target.Authority.SessionId
            || turn.Provenance.Request.Origin != snapshot.Origin
            || !string.Equals(turn.Context.UserRequest, snapshot.OriginalRequest, StringComparison.Ordinal)
            || !snapshot.Current || !customizationDiscoveries.TryAdd(turn, snapshot))
        { throw new InvalidOperationException("Discovery requires this exact original local request and its single-use reviewed metadata."); }
    }

    private async ValueTask<bool> CustomizationDiscoveryCurrent(ModelTurn turn, CancellationToken token) =>
        turn.Context.ToolMode != ModelToolMode.CustomizationDiscovery
        || customizationDiscoveries.TryGetValue(turn, out var snapshot) && await snapshot.Check(token).ConfigureAwait(false);
}
