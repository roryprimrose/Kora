using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

public sealed class ModelToolContext
{
    internal ModelToolContext(ModelTurnProvenance provenance, string systemPolicy, string userRequest,
        ImmutableArray<ModelContextEvidence> evidence, ModelToolMode toolMode, ImmutableArray<byte> wire,
        ModelTurnConstraints? constraints = null, Mcp.McpModelTool? mcpTool = null)
    {
        Provenance = provenance;
        SystemPolicy = systemPolicy;
        UserRequest = userRequest;
        Evidence = evidence;
        ToolMode = toolMode;
        Wire = wire;
        Constraints = constraints;
        McpTool = mcpTool;
    }

    internal ModelTurnProvenance Provenance { get; }
    public string SystemPolicy { get; }
    public string UserRequest { get; }
    public ImmutableArray<ModelContextEvidence> Evidence { get; }
    public ModelToolMode ToolMode { get; }
    public ModelTurnConstraints? Constraints { get; }
    public IReadOnlyList<Kora.Core.Tools.CapabilityDescriptor> Tools =>
        Constraints?.GetTools(ToolMode) ?? ModelToolProtocol.GetTools(ToolMode);
    public Mcp.McpModelTool? McpTool { get; }
    public ImmutableArray<byte> Wire { get; }
}
