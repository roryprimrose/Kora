using Avalonia.Threading;

using Kora.Application.Mcp;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private McpContentResult? mcpContent;
    private long mcpContentSelection;
    private long mcpContentEpoch;
    private long mcpContentControl;
    internal bool IsMcpCapabilityManagerOpen { get; private set; }

    public string McpContentText => HasCurrentMcpOptions && access.CanInspect && mcpContentControl == access.ControlRevision
        && mcpContentEpoch == selectionEpoch && mcpContentSelection == mcpSelectionRevision && mcpContent is { } content
        ? "Observed external " + content.Capability.Kind + ": " + content.Capability.Name
            + "\nObserved UTC: " + content.ObservedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            + "\nComplete inert external text; template roles are not conversation history:\n" + content.Document.Display
        : string.Empty;

    private void RetireMcpContent()
    {
        if (mcpContent is null) { return; }
        mcpContent = null;
        if (Dispatcher.UIThread.CheckAccess()) { OnPropertyChanged(nameof(McpContentText)); }
        else { Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(McpContentText))); }
    }

    public Task ManageMcpCapabilitiesAsync(RequestOrigin origin = RequestOrigin.LocalUi, Func<bool>? admitted = null) =>
        RunMcpCapabilityOperationAsync(null, false, null, origin, admitted);

    public Task UseMcpContentAsync(McpCapabilityKind kind, bool useModel, string? originalBody = null,
        RequestOrigin origin = RequestOrigin.LocalUi, Func<bool>? admitted = null) =>
        RunMcpCapabilityOperationAsync(kind, useModel, originalBody, origin, admitted);

    private Task RunMcpCapabilityOperationAsync(McpCapabilityKind? kind, bool useModel, string? originalBody,
        RequestOrigin origin, Func<bool>? admitted)
    {
        if (IsMcpActive || !CanCompose) { return Task.CompletedTask; }
        explicitMcpOperation = RunMcpCapabilityCoreAsync(kind, useModel, originalBody, origin, admitted);
        return explicitMcpOperation;
    }

    private async Task RunMcpCapabilityCoreAsync(McpCapabilityKind? kind, bool useModel, string? originalBody,
        RequestOrigin origin, Func<bool>? admitted)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        mcpCancellation = cancellation;
        mcpResult = null;
        OnPropertyChanged(nameof(IsMcpActive));
        try
        {
            await RunConversationAsync(async (target, live) =>
            {
                var server = SelectedMcpServer ?? throw new InvalidOperationException("Select an exact app-included MCP server.");
                var account = SelectedMcpAccount ?? throw new InvalidOperationException("Select an exact delegated MCP account.");
                var selection = mcpSelectionRevision;
                bool Current() => live() && (admitted?.Invoke() ?? true) && mcpSelectionRevision == selection;
                var present = McpPresenter ?? throw new InvalidOperationException("Native MCP review is unavailable.");
                if (kind is null)
                {
                    var inventory = await conversation!.DiscoverMcpCapabilitiesAsync(target, server.Id, account.Reference,
                        present, Current, cancellation.Token, origin);
                    if (!Current()) { throw new OperationCanceledException("MCP inventory presentation was retired.", cancellation.Token); }
                    McpCapabilityObserved?.Invoke(inventory);
                    var manager = McpCapabilityManager ?? throw new InvalidOperationException("The private MCP capability manager is unavailable.");
                    IsMcpCapabilityManagerOpen = true;
                    var change = await manager(inventory, Current, cancellation.Token);
                    if (!Current()) { throw new OperationCanceledException("The MCP capability review was retired."); }
                    if (change is null) { conversationStatus = "MCP preference review cancelled. Nothing was saved."; return; }
                    await conversation.SaveMcpCapabilitiesAsync(inventory, change.Selection, change.ServerIncluded, Current, cancellation.Token);
                    conversationStatus = "MCP consideration preferences saved and verified. Pending use requires fresh review."
                        + " Refresh options before another operation. No content was read and no model was invoked.";
                    mcpOptionsEpoch = -1;
                    return;
                }
                var selector = McpCapabilitySelector ?? throw new InvalidOperationException("Native MCP capability selection is unavailable.");
                var input = McpContentInput ?? throw new InvalidOperationException("Native MCP content input is unavailable.");
                McpContentResult? observed = null;
                if (useModel)
                {
                    var body = originalBody ?? composerBody;
                    var receipt = await conversation!.SubmitMcpContentAsync(target, body, server.Id, account.Reference,
                        kind.Value, present, selector, input, Current, cancellation.Token, origin,
                        (_, result) => observed = result);
                    if (!Current()) { throw new OperationCanceledException("The original MCP content answer was retired."); }
                    if (string.Equals(composerBody, body, StringComparison.Ordinal)) { composerBody = string.Empty; }
                    RememberUnsent();
                    PublishConversationCompletion(receipt.Message);
                    conversationStatus = "MCP text question: " + receipt.Message.Outcome
                        + (receipt.Message.AssistantResponse is { } response ? "\n" + response.Body : ". No assistant answer was retained.");
                }
                else
                {
                    var result = await conversation!.ReadMcpContentAsync(target, server.Id, account.Reference,
                        kind.Value, present, selector, input, Current, cancellation.Token, origin);
                    if (!Current()) { throw new OperationCanceledException("The original MCP content presentation was retired."); }
                    observed = result.Content;
                    conversationStatus = "MCP text retrieval: " + result.Outcome + "; durable task " + result.DurableState
                        + ". Transport joined. No model was invoked; unsent input is unchanged.";
                }
                mcpContent = observed;
                mcpContentSelection = selection;
                mcpContentEpoch = selectionEpoch;
                mcpContentControl = access.ControlRevision;
                OnPropertyChanged(nameof(McpContentText));
            }, submission: true, origin: origin, requireOriginalInput: true);
        }
        finally
        {
            IsMcpCapabilityManagerOpen = false;
            mcpCancellation = null;
            OnPropertyChanged(nameof(IsMcpActive));
        }
    }
}