using System.ComponentModel;
using Avalonia.Controls;
using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

internal sealed class McpCapabilityManagerWindow : Window
{
    internal McpCapabilityManagerEditorView View { get; }
    internal TextBlock CapabilityFilterSummary => View.CapabilityFilterSummary;

    internal McpCapabilityManagerWindow(McpCapabilityManagerView inventory)
    {
        Title = "MCP capabilities - " + inventory.ServerName;
        Width = 860; Height = 680;
        View = new(this, inventory, static () => true);
        Content = View;
    }

    internal void FilterCapabilities(string? filter, McpCapabilityCategory? category) => View.FilterCapabilities(filter, category);
    internal void ClearCapabilityFilter() => View.ClearCapabilityFilter();
    internal static Task<McpCapabilityChange?> ShowAsync(Window owner, INotifyPropertyChanged model,
        McpCapabilityInventory inventory, Func<bool> admitted, CancellationToken token) =>
        McpCapabilityManagerEditorView.ShowAsync(owner, model, inventory, admitted, token);
    internal static Task<McpCapabilityChange?> ShowAsync(Window owner, INotifyPropertyChanged model,
        McpCapabilityManagerView inventory, Func<bool> admitted, CancellationToken token,
        string? filter = null, McpCapabilityCategory? category = null) =>
        McpCapabilityManagerEditorView.ShowAsync(owner, model, inventory, admitted, token, filter, category);
}
