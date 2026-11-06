using Avalonia.Controls;

using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;

namespace Kora;

/// <summary>Shared native presentation of the existing MCP authorization and exact tool selection.</summary>
internal sealed class McpNativeReview(QuestionWindowController questions, Func<Window> owner)
{
    internal async Task<McpApprovalPresentation> PresentAsync(HostOperationProposal proposal, HostRevision generation,
        McpReview review, Func<bool> admitted, CancellationToken token)
    {
        var spec = new QuestionSpec("Exact HTTPS MCP approval", QuestionKind.SingleChoice, [new("once", "Approve once")]);
        var receipt = await questions.AskConversationAsync(owner(), proposal.Request, generation, spec,
            proposal.ExpiresAt, admitted, token, webProposal: proposal, mcpReview: review);
        return new(receipt.Presented, receipt.Completed);
    }

    internal async Task<string?> SelectAsync(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
        IReadOnlyList<McpDiscoveredTool> tools, Func<bool> admitted, CancellationToken token)
    {
        var choices = tools.Select(tool => new McpCapability(McpCapabilityKind.Tool, tool.Name, tool.Name, tool.Schema.Json)).ToArray();
        return (await SelectCapabilityAsync(request, generation, expiresAt, choices, admitted, token))?.Id;
    }

    internal async Task<McpCapability?> SelectCapabilityAsync(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
        IReadOnlyList<McpCapability> capabilities, Func<bool> admitted, CancellationToken token)
    {
        var offset = 0;
        const int pageSize = 30;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var options = capabilities.Skip(offset).Take(pageSize).Select((item, index) => new QuestionOption(
                "tool-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Name)).ToList();
            if (offset > 0) { options.Add(new("previous", "Previous page")); }
            if (offset + pageSize < capabilities.Count) { options.Add(new("next", "Next page")); }
            var spec = new QuestionSpec("Select one enabled, supported MCP item. Complete identity, definition and outgoing arguments"
                + " still require separate review. Page " + (offset / pageSize + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                QuestionKind.SingleChoice, options, sourceId: "mcp-tool-selection");
            var receipt = await questions.AskConversationAsync(owner(), request, generation, spec, expiresAt, admitted, token, mcpSelection: true);
            if (receipt.Completed.Outcome != HostInteractionOutcome.Answered
                || receipt.Completed.Question?.Draft is not { Choices.Length: 1 } answer)
            { return null; }
            if (string.Equals(answer.Choices[0], "next", StringComparison.Ordinal)) { offset += pageSize; continue; }
            if (string.Equals(answer.Choices[0], "previous", StringComparison.Ordinal)) { offset -= pageSize; continue; }
            var index = options.FindIndex(option => string.Equals(option.Id, answer.Choices[0], StringComparison.Ordinal));
            return index < 0 || index >= Math.Min(pageSize, capabilities.Count - offset) ? null : capabilities[offset + index];
        }
    }

    internal async Task<string?> ContentInputAsync(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
        McpCapability capability, Func<bool> admitted, CancellationToken token)
    {
        using var definition = System.Text.Json.JsonDocument.Parse(capability.DefinitionJson);
        var parameters = capability.Kind == McpCapabilityKind.ResourceTemplate
            ? string.Join(", ", new McpResourceTemplate(capability.Id).Parameters)
            : definition.RootElement.TryGetProperty("arguments", out var declared)
                ? string.Join(", ", declared.EnumerateArray().Select(argument => argument.GetProperty("name").GetString()
                    + (argument.TryGetProperty("required", out var required) && required.GetBoolean() ? " (required)" : " (optional)")))
                : "(none)";
        var spec = new QuestionSpec("Enter a JSON object of original string arguments for " + capability.Name
            + ". Use {} when none are needed. Submission is not approval.\nParameters / complete definition:\n" + parameters,
            QuestionKind.Text, [], maximumTextLength: McpToolSchema.MaximumArgumentsUtf8Bytes, sourceId: "mcp-tool-arguments");
        var receipt = await questions.AskConversationAsync(owner(), request, generation, spec, expiresAt, admitted, token, mcpInput: true);
        return receipt.Completed.Outcome == HostInteractionOutcome.Answered ? receipt.Completed.Question?.Draft?.Text : null;
    }

    internal async Task<string?> InputAsync(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
        McpModelTool tool, Func<bool> admitted, CancellationToken token)
    {
        using var schema = System.Text.Json.JsonDocument.Parse(tool.InputSchema);
        var spec = new QuestionSpec("Enter JSON arguments for " + tool.Id
            + ". Every required field must match this schema. Submitting input is NOT approval to send it.\n"
            + System.Text.Json.JsonSerializer.Serialize(schema.RootElement), QuestionKind.Text, [],
            maximumTextLength: McpToolSchema.MaximumArgumentsUtf8Bytes, sourceId: "mcp-tool-arguments");
        var receipt = await questions.AskConversationAsync(owner(), request, generation, spec, expiresAt,
            admitted, token, mcpInput: true);
        return receipt.Completed.Outcome == HostInteractionOutcome.Answered ? receipt.Completed.Question?.Draft?.Text : null;
    }
}