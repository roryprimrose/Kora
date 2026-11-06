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
        var options = tools.Select((tool, index) => new QuestionOption(
            "tool-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), tool.Name)).ToArray();
        var spec = new QuestionSpec("Select one discovered host-mapped read-only MCP tool. "
            + "Its complete schema and outgoing arguments still require separate exact review.",
            QuestionKind.SingleChoice, options, sourceId: "mcp-tool-selection");
        var receipt = await questions.AskConversationAsync(owner(), request, generation, spec, expiresAt,
            admitted, token, mcpSelection: true);
        if (receipt.Completed.Outcome != HostInteractionOutcome.Answered
            || receipt.Completed.Question?.Draft is not { Choices.Length: 1 } answer)
        { return null; }
        var selected = Array.FindIndex(options, option => string.Equals(option.Id, answer.Choices[0], StringComparison.Ordinal));
        return selected < 0 ? null : tools[selected].Name;
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
