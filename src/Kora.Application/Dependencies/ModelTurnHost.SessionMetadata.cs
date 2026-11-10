using System.Runtime.CompilerServices;

using Kora.Application.Tools;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, SessionMetadataInvocation> sessionMetadata = new();

    internal void BindSessionMetadata(ModelTurn turn, SessionMetadataInvocation invocation)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.SessionMetadata
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || !sessionMetadata.TryAdd(turn, invocation))
        { throw new InvalidOperationException("Metadata requires the original local host turn and separate one-use review."); }
    }

    private async ValueTask<bool> SessionMetadataCurrentAsync(ModelTurn turn, CancellationToken token) =>
        turn.Context.ToolMode != ModelToolMode.SessionMetadata
        || sessionMetadata.TryGetValue(turn, out var invocation) && await invocation.ValidateAsync(token).ConfigureAwait(false);
}
