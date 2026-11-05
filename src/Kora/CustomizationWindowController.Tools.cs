using System.Collections.Immutable;

using Avalonia.Threading;

using Kora.Application.Configuration;
using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private readonly Dictionary<string, McpCapabilityInventory> externalInventories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McpDisplayedServer> displayedServers = new(StringComparer.Ordinal);
    private long capabilityPresentationRevision;

    internal void ObserveMcpCapabilities(McpCapabilityInventory inventory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { Dispatcher.UIThread.Post(() => ObserveMcpCapabilities(inventory)); return; }
        if (!Eligible() || mcpConfiguration is null) { return; }
        externalInventories[inventory.ServerId] = inventory;
        displayedServers[inventory.ServerId] = McpDisplayedServer.FromInventory(inventory,
            mcpConfiguration.IsServerSelected(inventory.ServerId), true);
        Interlocked.Increment(ref capabilityPresentationRevision);
        RenderSources();
    }

    private void RefreshCapabilityMetadata()
    {
        if (mcpConfiguration is null) { return; }
        var selection = mcpConfiguration.ReadCapabilitySelection();
        var servers = mcpConfiguration.LoadCapabilities();
        displayedServers.Clear();
        foreach (var server in servers)
        {
            if (server.IsBuiltIn)
            {
                displayedServers.Add(server.Id, new(new(server.Id, server.Name,
                    mcpConfiguration.ReadBuiltInTools().ToImmutableArray(), selection, new(true, false, false),
                    mcpConfiguration.IsServerSelected(server.Id)), server.Source, "Built-in", server.IsIncluded));
            }
            else if (externalInventories.TryGetValue(server.Id, out var inventory)
                && inventory.Source.SourceControlRevision == mcpConfiguration.SourceControlRevision(server.Id))
            {
                displayedServers.Add(server.Id, McpDisplayedServer.FromInventory(inventory,
                    mcpConfiguration.IsServerSelected(server.Id), server.IsIncluded));
            }
        }
        foreach (var id in externalInventories.Keys.Where(id => !displayedServers.ContainsKey(id)).ToArray())
        { externalInventories.Remove(id); }
        Interlocked.Increment(ref capabilityPresentationRevision);
    }

    private async Task ManageCapabilities(Func<bool> current, CancellationToken token)
    {
        if (window?.SelectedCapability is not { } selected || mcpConfiguration is null || mcpSettings is null
            || !displayedServers.TryGetValue(selected.ServerId, out var displayed))
        { throw new InvalidOperationException("Select an exact admitted MCP capability row before managing its server."); }
        var revision = capabilityPresentationRevision;
        var sourceRevision = mcpConfiguration.CaptureCapabilityRevision(selected.ServerId);
        bool Current() => current() && revision == capabilityPresentationRevision;
        var change = await McpCapabilityManagerWindow.ShowAsync(window, main, displayed.View, Current, token);
        RequireCurrent(Current, token);
        if (change is null) { return; }
        if (!string.Equals(sourceRevision, mcpConfiguration.CaptureCapabilityRevision(selected.ServerId), StringComparison.Ordinal))
        { throw new InvalidOperationException("MCP preferences changed during review. Refresh before another proposal."); }
        if (!string.Equals(selected.ServerId, KoraMcpCatalog.ServerId, StringComparison.Ordinal))
        {
            if (!externalInventories.TryGetValue(selected.ServerId, out var inventory) || mcpConversation is null)
            { throw new InvalidOperationException("External MCP metadata needs a fresh explicit Session discovery."); }
            await mcpConversation.SaveMcpCapabilitiesAsync(inventory, change.Selection, change.ServerIncluded, Current, token);
        }
        else
        {
            RequireCurrent(Current, token);
            mcpSettings.SetMcpCapabilities(selected.ServerId, change.ServerIncluded, change.Selection,
                displayed.View.Selection.Fingerprint(selected.ServerId));
        }
        RequireCurrent(current, token);
        RefreshCapabilityMetadata();
        RenderSources();
        window.Status.Text = "MCP consideration preferences saved and verified. Existing authority, source-use vetoes and separate approvals are unchanged.";
    }

    private void RetireCapabilityMetadata(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref capabilityPresentationRevision);
        Dispatcher.UIThread.Post(() =>
        {
            externalInventories.Clear();
            displayedServers.Clear();
            RenderSources();
        });
    }
}