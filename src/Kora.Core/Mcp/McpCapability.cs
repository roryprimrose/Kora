namespace Kora.Core.Mcp;

/// <summary>Bounded native inventory metadata, never host policy or model context.</summary>
public sealed record McpCapability(McpCapabilityKind Kind, string Id, string Name, string DefinitionJson,
    string? UnavailableReason = null)
{
    public McpCapabilityCategory Category => McpCapabilitySelection.Category(Kind);
    public override string ToString() => Name + (UnavailableReason is null ? string.Empty : " (unavailable: " + UnavailableReason + ")");
}
