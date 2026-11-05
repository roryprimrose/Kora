using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.Mcp;

internal sealed partial class McpInvocation
{
    private McpServerCapabilities? serverCapabilities;
    private McpCapability? contentCapability;
    private McpContentContinuationReview? contentContinuation;
    private int nextRequestId = 2;
    private int discoveryPages;
    private int discoveryBytes;
    internal McpTextDocument? Content { get; private set; }
    internal DateTimeOffset ContentObservedAt { get; private set; }
    internal McpCapability? ContentCapability => contentCapability;
    internal McpConnectionDescriptor Source => descriptor ?? throw new InvalidOperationException("No MCP source is prepared.");
    internal McpServerCapabilities DeclaredCapabilities => serverCapabilities
        ?? throw new InvalidOperationException("No MCP capabilities were negotiated.");

    private async Task PrepareConnectionAsync(CancellationToken token, McpCapabilityCategory? category = null)
    {
        if (descriptor is not null)
        { throw new InvalidOperationException("MCP preparation is single-use; start a fresh reviewed request."); }
        await ValidateAsync(token).ConfigureAwait(false);
        descriptor = await configuration.ResolveHttpsAsync(serverId, account, token).ConfigureAwait(false);
        if (category is { } selectedCategory && !configuration.MayConsiderCategory(serverId, selectedCategory))
        { throw new InvalidOperationException("This MCP capability category is disabled."); }
        if (skill is { } binding && (!string.Equals(binding.Reference.ServerId, descriptor.ServerId, StringComparison.Ordinal)
            || !string.Equals(binding.Reference.SourceRevision, descriptor.SourceRevision, StringComparison.Ordinal)))
        { throw new InvalidOperationException("The selected MCP source does not match the skill's exact declared dependency."); }
        var accounts = await broker.ReadAccountsAsync(token).ConfigureAwait(false);
        accountChoice = accounts.SingleOrDefault(choice => choice.Reference == account)
            ?? throw new InvalidOperationException("The explicitly selected delegated MCP account is not available.");
        await ValidateAsync(token).ConfigureAwait(false);
        await ApproveAsync(McpReviewStage.Connection, null, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        connection = await broker.ConnectAsync(account, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        var initialize = await ExchangeAsync(McpProtocol.InitializeRequest(), McpProtocol.MaximumDiscoveryUtf8Bytes, token).ConfigureAwait(false);
        serverCapabilities = McpCapabilityProtocol.ReadInitialization(initialize.AsSpan());
        await connection.NotifyAsync(ImmutableArray.CreateRange(McpProtocol.InitializedNotification()), token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
    }

    private async Task<ImmutableArray<McpCapability>> DiscoverKindAsync(McpCapabilityKind kind, CancellationToken token)
    {
        if (!DeclaredCapabilities.Supports(kind)) { return []; }
        var items = ImmutableArray.CreateBuilder<McpCapability>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            token.ThrowIfCancellationRequested();
            if (++discoveryPages > McpCapabilityProtocol.MaximumPages)
            { throw new InvalidDataException("The MCP inventory exceeds its page bound. No complete inventory was produced."); }
            var id = nextRequestId++;
            var bytes = await ExchangeAsync(McpCapabilityProtocol.ListRequest(kind, id, cursor),
                McpProtocol.MaximumDiscoveryUtf8Bytes, token).ConfigureAwait(false);
            discoveryBytes += bytes.Length;
            if (discoveryBytes > McpCapabilityProtocol.MaximumInventoryBytes)
            { throw new InvalidDataException("The complete MCP inventory exceeds its byte bound."); }
            var page = McpCapabilityProtocol.ReadPage(bytes.AsSpan(), kind, id);
            foreach (var item in page.Items)
            {
                if (!identities.Add(item.Id) || items.Count >= McpCapabilityProtocol.MaximumItems)
                { throw new InvalidDataException("The MCP inventory contains duplicate identities or exceeds its item bound."); }
                items.Add(item);
            }
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor))
            { throw new InvalidDataException("The MCP server repeated a discovery cursor."); }
        } while (cursor is not null);
        return items.ToImmutable();
    }

    internal async Task<ImmutableArray<McpCapability>> DiscoverAllAsync(CancellationToken token)
    {
        await PrepareConnectionAsync(token).ConfigureAwait(false);
        var all = ImmutableArray.CreateBuilder<McpCapability>();
        foreach (var kind in Enum.GetValues<McpCapabilityKind>())
        {
            var discovered = await DiscoverKindAsync(kind, token).ConfigureAwait(false);
            foreach (var item in discovered)
            {
                var effective = item;
                if (kind == McpCapabilityKind.Tool && item.UnavailableReason is null)
                {
                    using var definition = JsonDocument.Parse(item.DefinitionJson);
                    var tool = new McpDiscoveredTool(item.Id, new(definition.RootElement.GetProperty("inputSchema").GetRawText()));
                    if (effects.Find(Source, tool) is null)
                    { effective = item with { UnavailableReason = "No current host-owned read-only effect mapping." }; }
                }
                all.Add(effective);
            }
            if (all.Count > McpCapabilityProtocol.MaximumItems)
            { throw new InvalidDataException("The complete MCP inventory exceeds its item bound."); }
        }
        await JoinTransportAsync().ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        Completed = true;
        return all.ToImmutable();
    }

    internal async Task<CapabilityReply> ReadContentAsync(McpCapabilityKind kind, McpCapabilitySelector select,
        McpContentInputPresenter input, CancellationToken token)
    {
        if (kind is not (McpCapabilityKind.Resource or McpCapabilityKind.ResourceTemplate or McpCapabilityKind.Prompt))
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        await PrepareConnectionAsync(token, McpCapabilitySelection.Category(kind)).ConfigureAwait(false);
        var discovered = await DiscoverKindAsync(kind, token).ConfigureAwait(false);
        var eligible = discovered.Where(item => item.UnavailableReason is null
            && configuration.IsCapabilityEnabled(serverId, item.Kind, item.Id)).ToArray();
        if (eligible.Length == 0) { throw new InvalidOperationException("No supported enabled MCP item is available in this category."); }
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(interactionRequest, Core.Diagnostics.HostActivityLayer.Application,
            Core.Diagnostics.HostOperation.Tool, [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var choice = await select(interactionRequest, generation, expiresAt, eligible,
                () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            if (choice is null) { throw new McpSelectionCancelledException(token); }
            contentCapability = eligible.SingleOrDefault(item => item == choice)
                ?? throw new InvalidOperationException("Choose an exact current presented MCP capability.");
            await ApproveAsync(McpReviewStage.ToolSelection, null, token).ConfigureAwait(false);
            var arguments = kind == McpCapabilityKind.Resource ? "{}"
                : await input(interactionRequest, generation, expiresAt, contentCapability,
                    () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            if (arguments is null) { throw new OperationCanceledException("MCP arguments were not submitted.", token); }
            Core.Conversation.ConversationText.Validate(arguments, submission: true);
            var uri = kind == McpCapabilityKind.ResourceTemplate ? new McpResourceTemplate(contentCapability.Id).Expand(arguments)
                : kind == McpCapabilityKind.Resource ? contentCapability.Id : null;
            if (kind == McpCapabilityKind.ResourceTemplate
                && configuration.IsCapabilityExplicitlyDisabled(serverId, McpCapabilityKind.Resource, uri!))
            { throw new InvalidOperationException("The resolved resource is excluded from Kora use."); }
            var id = nextRequestId++;
            var wire = kind == McpCapabilityKind.Prompt
                ? McpCapabilityProtocol.PromptRequest(contentCapability, arguments, id)
                : McpCapabilityProtocol.ResourceRequest(uri!, id);
            var reviewedArguments = kind == McpCapabilityKind.Prompt ? arguments
                : JsonSerializer.Serialize(new { Parameters = arguments, ResourceUri = uri });
            await ApproveAsync(McpReviewStage.Invocation, reviewedArguments, token).ConfigureAwait(false);
            var result = await ExchangeAsync(wire, McpCapabilityProtocol.MaximumContentBytes, token).ConfigureAwait(false);
            var document = kind == McpCapabilityKind.Prompt ? McpCapabilityProtocol.ReadPrompt(result.AsSpan(), id)
                : McpCapabilityProtocol.ReadResource(result.AsSpan(), id, uri!);
            foreach (var block in document.Blocks)
            {
                Core.Conversation.ConversationText.RequireNoCredentials(block.Text);
                if (kind == McpCapabilityKind.Prompt && block.Uri is { } resource
                    && !configuration.IsCapabilityEnabled(serverId, McpCapabilityKind.Resource, resource))
                { throw new InvalidOperationException("The prompt contains a resource excluded from Kora use. No part was accepted."); }
            }
            await JoinTransportAsync().ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            Content = document;
            ContentObservedAt = time.GetUtcNow();
            Completed = true;
            activity.Complete(HostOperationOutcome.Completed);
            return new(CapabilityOutcome.Succeeded, "observed-complete-inert-mcp-text");
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    internal async Task ConsentContentAsync(string question, string conversationName, CancellationToken token)
    {
        var content = Content ?? throw new InvalidOperationException("No complete MCP text was observed.");
        var risk = LocalSourceRiskPolicy.Evaluate([question, content.Json, Source.ServerName,
            Source.Endpoint.Address.AbsoluteUri, accountChoice!.DisplayName], token);
        if (risk != LocalSourceRiskOutcome.AdmittedPrivateLocalOnly)
        { throw new InvalidOperationException("This MCP content is unavailable for private local model use."); }
        contentContinuation = new(contentCapability!, content, ContentObservedAt, question, conversationName,
            original.Origin, LocalSourceRiskPolicy.Revision);
        if (!await ApproveAsync(McpReviewStage.ResultContinuation, null, token, contentReview: contentContinuation).ConfigureAwait(false))
        { throw new InvalidOperationException("Separate one-use local MCP content consent was not given."); }
        await ValidateAsync(token).ConfigureAwait(false);
    }
}