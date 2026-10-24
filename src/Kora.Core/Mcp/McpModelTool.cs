namespace Kora.Core.Mcp;

/// <summary>Exact request-owned model description; endpoint and account metadata never enter this value.</summary>
public sealed class McpModelTool
{
    public McpModelTool(string id, string inputSchema)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!id.StartsWith("mcp.", StringComparison.Ordinal))
        { throw new InvalidDataException("An external model tool requires its separate MCP namespace."); }
        McpToolSchema.ValidateName(id[4..]);
        _ = new McpToolSchema(inputSchema);
        Id = id;
        InputSchema = inputSchema;
    }

    public string Id { get; }
    public string InputSchema { get; }
}
