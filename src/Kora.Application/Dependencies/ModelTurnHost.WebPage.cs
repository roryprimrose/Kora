using System.Runtime.CompilerServices;
using Kora.Application.Network;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, LocalWebPageWorkflow> webPages = new();

    internal void BindWebPage(ModelTurn turn, LocalWebPageInvocation invocation)
        => BindWebPage(turn, new LocalWebPageWorkflow([(invocation.Destination, invocation)]));

    internal void BindWebPage(ModelTurn turn, LocalWebPageWorkflow invocation)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.LocalWebPage
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama || !webPages.TryAdd(turn, invocation))
        { throw new InvalidOperationException("Only a fresh exact local web-page turn can bind a destination."); }
    }
}
