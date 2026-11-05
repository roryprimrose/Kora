using System.Runtime.CompilerServices;

using Kora.Application.Mcp;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, McpInvocation> mcpInvocations = new();

    internal void BindMcp(ModelTurn turn, McpInvocation invocation)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.McpReadOnly
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || !string.Equals(turn.Context.McpTool!.Id, invocation.ModelTool.Id, StringComparison.Ordinal)
            || !string.Equals(turn.Context.McpTool.InputSchema, invocation.ModelTool.InputSchema, StringComparison.Ordinal)
            || !mcpInvocations.TryAdd(turn, invocation))
        { throw new InvalidOperationException("Only a fresh exact original local MCP turn can bind the reviewed tool."); }
        invocation.BindTurn(turn);
    }

    internal void BindMcpContent(ModelTurn turn, McpInvocation invocation)
    {
        if (turn.Owner != owner || turn.Context.ToolMode != ModelToolMode.None
            || turn.Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || invocation.Content is null || !mcpInvocations.TryAdd(turn, invocation))
        { throw new InvalidOperationException("Only a fresh exact local MCP text turn may bind its reviewed source."); }
        invocation.BindTurn(turn);
    }
}