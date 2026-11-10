using Kora.Application.Skills;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Mcp;

namespace Kora;

internal static class McpSkillInvocationPresenter
{
    internal static Task<ReviewedMcpSkillInvocation> PrepareAsync(ReviewedToolSkillInvocationWorkflow service,
        ReviewedToolSkillReview review, SharedSkillInvocationWindow window, Func<bool> current,
        RequestOrigin origin, CancellationToken token) => PrepareAsync(service, review, window.View, current, origin, token);

    internal static async Task<ReviewedMcpSkillInvocation> PrepareAsync(ReviewedToolSkillInvocationWorkflow service,
        ReviewedToolSkillReview review, SharedSkillInvocationView window, Func<bool> current,
        RequestOrigin origin, CancellationToken token)
    {
        var server = window.McpServers.SelectedItem as Kora.Application.Configuration.McpServerConfigurationSummary
            ?? throw new InvalidOperationException("Select an exact current included MCP server.");
        var account = window.McpAccounts.SelectedItem as McpAccountChoice
            ?? throw new InvalidOperationException("Select an exact current delegated MCP account.");
        var invocation = await service.PrepareMcpAsync(review, window.Request.Text ?? "",
            server.Id, account.Reference, window.McpArguments.Text ?? "", current, origin, token);
        if (!current()) { invocation.Dispose(); throw new OperationCanceledException("Exact MCP review changed."); }
        window.Confirmation.Text = $"{review.SourceDescription}\n{review.Text}\n\nTARGET {review.Target.Authority.SessionId.Value:D}\n"
            + $"ORIGIN {review.Origin}\nSERVER {server.Display}\nACCOUNT {account.DisplayName}\n"
            + $"ORIGINAL REQUEST\n{invocation.Body}\nEXACT JSON ARGUMENTS\n{invocation.Arguments}\n"
            + "Confirm one local skill continuation. Native connection, current schema/read-only mapping and outgoing invocation approvals remain mandatory.";
        window.Confirm.IsEnabled = true;
        window.Status.Text = "Review the complete source, request, selected account and arguments. Nothing connected.";
        return invocation;
    }

    internal static Task<ConversationSubmissionReceipt> InvokeAsync(ReviewedToolSkillInvocationWorkflow service,
        ReviewedMcpSkillInvocation invocation, SharedSkillInvocationWindow window, QuestionWindowController questions,
        Func<bool> current, CancellationToken token, Action<HostRequest, McpModelResult>? result = null,
        McpResultCapture? capture = null) => InvokeAsync(service, invocation, window.View, questions, current, token, result, capture);

    internal static async Task<ConversationSubmissionReceipt> InvokeAsync(ReviewedToolSkillInvocationWorkflow service,
        ReviewedMcpSkillInvocation invocation, SharedSkillInvocationView window, QuestionWindowController questions,
        Func<bool> current, CancellationToken token, Action<HostRequest, McpModelResult>? result = null,
        McpResultCapture? capture = null)
    {
        window.Confirm.IsEnabled = false;
        var native = new McpNativeReview(questions, () => window.Owner);
        using var request = HostActivity.BeginRoot(HostRequest.Create(invocation.Review.Origin), HostActivityLayer.Desktop, HostOperation.Request);
        var receipt = await service.InvokeMcpAsync(invocation, current,
            (proposal, generation, mapping, admitted, check) => capture is null
                ? native.PresentAsync(proposal, generation, mapping, admitted, check)
                : capture.PresentAsync(native.PresentAsync, proposal, generation, mapping, admitted, check),
            native.SelectAsync, token, result);
        request.Complete(HostOperationOutcome.Completed);
        return receipt;
    }
}
