using Kora.Core.Mcp;

namespace Kora.Core.Skills;

/// <summary>Exact dependency data, never server inclusion, account selection or execution authority.</summary>
public sealed record DeclarativeMcpToolReference
{
    public const string Format = "kora-mcp-read-only-v1";

    public DeclarativeMcpToolReference(string serverId, string sourceRevision, string toolName, string schemaDigest)
    {
        RequireDigest(serverId);
        RequireDigest(sourceRevision);
        RequireDigest(schemaDigest);
        McpToolSchema.ValidateName(toolName);
        ServerId = serverId;
        SourceRevision = sourceRevision;
        ToolName = toolName;
        SchemaDigest = schemaDigest;
    }

    public string ServerId { get; }
    public string SourceRevision { get; }
    public string ToolName { get; }
    public string SchemaDigest { get; }
    public string ProtocolVersion => McpProtocol.Version;

    private static void RequireDigest(string value)
    {
        if (value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        { throw new InvalidDataException("An exact lowercase SHA-256 MCP source or schema identity is required."); }
    }
}
