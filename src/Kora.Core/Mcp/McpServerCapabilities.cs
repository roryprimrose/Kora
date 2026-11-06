namespace Kora.Core.Mcp;

public sealed record McpServerCapabilities(bool Tools, bool Resources, bool Prompts)
{
    public bool Supports(McpCapabilityKind kind)
    {
        return kind switch
        {
            McpCapabilityKind.Tool => Tools,
            McpCapabilityKind.Resource or McpCapabilityKind.ResourceTemplate => Resources,
            McpCapabilityKind.Prompt => Prompts,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}