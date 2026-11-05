using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

/// <summary>Private admitted metadata and saved/effective projections, never an invocation grant.</summary>
internal sealed record McpDisplayedServer(McpCapabilityManagerView View, string SourceLabel,
    string ScopeLabel, bool IsEffective, string? UnavailableReason = null)
{
    internal IEnumerable<McpCapabilityRow> Rows => View.Items.Select(capability =>
    {
        var selected = View.Selection.IsEnabled(View.ServerId, capability.Kind, capability.Id);
        return new McpCapabilityRow(View.ServerId, View.ServerName, SourceLabel, ScopeLabel, capability,
            selected, selected && IsEffective && capability.UnavailableReason is null,
            UnavailableReason ?? capability.UnavailableReason);
    });

    internal static McpDisplayedServer FromInventory(McpCapabilityInventory inventory, bool selected, bool effective) =>
        new(McpCapabilityManagerView.FromInventory(inventory) with { ServerIncluded = selected },
            inventory.Source.SourcePath, inventory.Source.Scope is { } scope ? scope.ToString() : "External", effective);

    public override string ToString() => nameof(McpDisplayedServer);
}