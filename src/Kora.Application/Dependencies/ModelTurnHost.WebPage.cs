using System.Runtime.CompilerServices;
using Kora.Application.Network;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, LocalWebPageInvocation> webPages = new();

    internal void BindWebPage(ModelTurn turn, LocalWebPageInvocation invocation)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.LocalWebPage
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama || !webPages.TryAdd(turn, invocation))
        { throw new InvalidOperationException("Only a fresh exact local web-page turn can bind a destination."); }
    }
}
