using System.Collections.Immutable;

using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

/// <summary>Native presentation data, separate from an external discovery receipt or connection authority.</summary>
internal sealed record McpCapabilityManagerView(string ServerId, string ServerName,
    ImmutableArray<McpCapability> Items, McpCapabilitySelection Selection,
    McpServerCapabilities Capabilities, bool ServerIncluded)
{
    internal static McpCapabilityManagerView FromInventory(McpCapabilityInventory inventory) =>
        new(inventory.ServerId, inventory.ServerName, inventory.Items, inventory.Selection, inventory.Capabilities, true);
    public override string ToString() => nameof(McpCapabilityManagerView);
}