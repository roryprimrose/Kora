using System.Collections.Immutable;

namespace Kora.Core.Mcp;

public sealed class McpCategorySelection
{
    public McpCategorySelection(string serverId, McpCapabilityCategory category, bool enabled,
        IEnumerable<McpItemSelection> items, long revision = 0)
    {
        McpCapabilitySelection.ValidateServer(serverId);
        McpCapabilitySelection.ValidateCategory(category);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Items = items.Take(McpCapabilitySelection.MaximumOverrides + 1).ToImmutableArray();
        if (Items.Length > McpCapabilitySelection.MaximumOverrides || Items.Any(item => item is null)
            || Items.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != Items.Length)
        { throw new ArgumentException("MCP item overrides must be unique and bounded.", nameof(items)); }
        ServerId = serverId;
        Category = category;
        Enabled = enabled;
        Revision = revision;
    }

    public string ServerId { get; }
    public McpCapabilityCategory Category { get; }
    public bool Enabled { get; }
    public long Revision { get; }
    public ImmutableArray<McpItemSelection> Items { get; }
}