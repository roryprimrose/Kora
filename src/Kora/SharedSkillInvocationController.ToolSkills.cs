using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Skills;
using Kora.Core.Network;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SharedSkillInvocationController
{
    private bool toolMode;
    private SharedSkillSource? toolSource;
    private SharedSkillSnapshot? toolSnapshot;
    private ReviewedToolSkillReview? toolReview;
    private ReviewedToolSkillInvocation? toolInvocation;
    private LocalWebPageScope? toolScope;
    private ReviewedMcpSkillInvocation? mcpInvocation;

    private async Task RunTool(string action, RequestOrigin origin, Func<bool> eligible)
    {
        var service = toolWorkflow ?? throw new InvalidOperationException("The reviewed one-tool workflow is not composed.");
        var opened = window ?? throw new InvalidOperationException("Review window is closed.");
        var token = lifetime!.Token;
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened);
        if (!Current()) { throw new InvalidOperationException("Original review admission expired."); }
        if (action is "McpOptions")
        {
            if (toolSnapshot?.McpReference is null)
            { throw new InvalidOperationException("Select one exact declarative MCP skill first."); }
            RetireConfirmation();
            RetireToolReview();
            opened.McpServers.ItemsSource = null;
            opened.McpAccounts.ItemsSource = null;
            var servers = service.ReadMcpServers();
            var accounts = await service.ReadMcpAccountsAsync(token);
            if (!Current()) { throw new OperationCanceledException("MCP selection admission changed."); }
            opened.McpServers.ItemsSource = servers;
            opened.McpAccounts.ItemsSource = accounts;
            opened.Status.Text = "Independently select one current included server and delegated account. Nothing connected or invoked.";
            return;
        }
        if (action is "Targets" or "NextTargets")
        {
            var after = action is "NextTargets" ? next ?? throw new InvalidOperationException("No next page; refresh targets.") : (Guid?)null;
            RetireConfirmation();
            RetireToolReview();
            opened.Targets.ItemsSource = null;
            var page = await workflow.ReadTargetsAsync(after, Current, origin, token);
            if (!Current()) { return; }
            next = page.Next;
            opened.Targets.ItemsSource = page.Records.Where(item => item.Authority.IsActive)
                .Select(item => new SharedSkillConversationTarget(item)).ToArray();
            opened.Status.Text = "Select a target, then review the exact target and tool revision.";
            return;
        }
        if (action is "ReviewToolRevision")
        {
            RetireConfirmation();
            RetireToolReview();
            if (origin != requestOrigin)
            { throw new InvalidOperationException("Review must use the original request channel; enter a fresh request before changing channels."); }
            var target = (opened.Targets.SelectedItem as SharedSkillConversationTarget)?.Entry
                ?? throw new InvalidOperationException("Select exactly one existing Active conversation.");
            if (toolSnapshot?.McpReference is not null)
            {
                _ = opened.McpServers.SelectedItem as Kora.Application.Configuration.McpServerConfigurationSummary
                    ?? throw new InvalidOperationException("Independently select an included MCP server.");
                _ = opened.McpAccounts.SelectedItem as Kora.Core.Mcp.McpAccountChoice
                    ?? throw new InvalidOperationException("Independently select a delegated MCP account.");
                ConversationText.Validate(opened.Request.Text ?? string.Empty, submission: true);
                var mcpRevision = toolInputRevision;
                toolReview = await service.ReviewAsync(toolSource!, toolSnapshot!, target,
                    () => Current() && toolInputRevision == mcpRevision, requestOrigin, token);
                if (!Current() || toolInputRevision != mcpRevision) { RetireToolReview(); return; }
                opened.Confirmation.Text = "Exact skill and target reviewed. Current source, account, discovered schema and arguments "
                    + "still need the separate native MCP reviews before any tool call.";
                opened.Status.Text = "Separately enable this revision's local context. No MCP connection or action permission granted.";
                return;
            }
            if (!Uri.TryCreate(opened.Address.Text, UriKind.Absolute, out var first)
                || !string.IsNullOrEmpty(opened.SecondAddress.Text)
                    && !Uri.TryCreate(opened.SecondAddress.Text, UriKind.Absolute, out _))
            { throw new InvalidOperationException("Enter one or two distinct absolute HTTP or HTTPS source URLs; model output cannot select them."); }
            var scope = new LocalWebPageScope(string.IsNullOrEmpty(opened.SecondAddress.Text)
                ? [first] : [first, new Uri(opened.SecondAddress.Text, UriKind.Absolute)]);
            ConversationText.Validate(opened.Request.Text ?? string.Empty, submission: true);
            var revision = toolInputRevision;
            toolReview = await service.ReviewAsync(toolSource!, toolSnapshot!, target,
                () => Current() && toolInputRevision == revision, requestOrigin, token);
            if (!Current()) { RetireToolReview(); return; }
            toolScope = scope;
            opened.Confirmation.Text = Describe(scope) + $"\nORIGINAL REQUEST\n{opened.Request.Text}";
            opened.Status.Text = "Exact source, target, inputs and finite web scope reviewed. Separately enable the local model context; no action permission is granted.";
            return;
        }
        var captured = toolReview ?? throw new InvalidOperationException("Review the exact target and tool revision first.");
        bool Reviewed() => Current() && captured.IsCurrent && ReferenceEquals(toolReview, captured);
        if (origin != captured.Origin || requestOrigin != captured.Origin)
        { RetireToolReview(); throw new InvalidOperationException("The original input channel changed; review again."); }
        if (action is "Enable")
        {
            await service.EnableAsync(captured, Reviewed, origin, token);
            if (Reviewed()) { opened.Status.Text = captured.McpReference is null
                ? "One-tool revision enabled for one local-only use. Web access still requires its own per-action authorization."
                : "Exact MCP skill revision enabled for one local-only use. MCP connection and invocation still require separate native authorization."; }
        }
        else if (action is "Prepare")
        {
            RetireConfirmation();
            if (captured.McpReference is not null)
            {
                var server = opened.McpServers.SelectedItem as Kora.Application.Configuration.McpServerConfigurationSummary
                    ?? throw new InvalidOperationException("Select an exact current included MCP server.");
                var account = opened.McpAccounts.SelectedItem as Kora.Core.Mcp.McpAccountChoice
                    ?? throw new InvalidOperationException("Select an exact current delegated MCP account.");
                var mcpRevision = inputRevision;
                mcpInvocation = await service.PrepareMcpAsync(captured, opened.Request.Text ?? string.Empty,
                    server.Id, account.Reference, opened.McpArguments.Text ?? string.Empty,
                    () => Reviewed() && inputRevision == mcpRevision, requestOrigin, token);
                if (!Reviewed() || inputRevision != mcpRevision) { RetireConfirmation(); return; }
                opened.Confirmation.Text = $"{opened.Instructions.Text}\n\nTARGET {captured.Target.Authority.SessionId.Value:D}\n"
                    + $"ORIGIN {captured.Origin}\nSERVER {server.Display}\nACCOUNT {account.DisplayName}\n"
                    + $"ORIGINAL REQUEST\n{mcpInvocation.Body}\nEXACT JSON ARGUMENTS\n{mcpInvocation.Arguments}\n"
                    + "Confirm one local skill continuation. Normal native connection, current tool/schema and outgoing invocation approvals remain mandatory.";
                opened.Confirm.IsEnabled = true;
                opened.Status.Text = "Review the complete skill, original request, selected source/account and arguments. Nothing connected.";
                return;
            }
            var revision = inputRevision;
            using var request = HostActivity.BeginRoot(HostRequest.Create(requestOrigin), HostActivityLayer.Desktop, HostOperation.Request);
            toolInvocation = await service.PrepareAsync(captured, opened.Request.Text ?? string.Empty,
                toolScope ?? throw new InvalidOperationException("Review the finite web scope first."),
                () => Reviewed() && inputRevision == revision, requestOrigin, token);
            request.Complete(HostOperationOutcome.Completed);
            if (!Reviewed() || inputRevision != revision) { RetireConfirmation(); return; }
            opened.Confirmation.Text = $"{opened.Instructions.Text}\n\nTARGET {captured.Target.Authority.SessionId.Value:D}\n"
                + $"ORIGIN {captured.Origin}\n{Describe(toolInvocation.Scope)}\nORIGINAL REQUEST\n{toolInvocation.Body}";
            opened.Confirm.IsEnabled = true;
            opened.Status.Text = "Confirm this exact revision, local model context, original request and existing target.";
        }
        else if (action is "Invoke")
        {
            if (captured.McpReference is not null)
            {
                var preparedMcp = mcpInvocation ?? throw new InvalidOperationException("Review this exact MCP skill invocation first.");
                mcpInvocation = null;
                opened.Confirm.IsEnabled = false;
                var mcpRevision = inputRevision;
                var native = new McpNativeReview(questions ?? throw new InvalidOperationException("Native MCP review is unavailable."), () => opened);
                using var mcpRequest = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
                var mcpReceipt = await service.InvokeMcpAsync(preparedMcp, () => Reviewed() && inputRevision == mcpRevision,
                    native.PresentAsync, native.SelectAsync, token, (_, result) =>
                    {
                        if (!Current() || inputRevision != mcpRevision) { return; }
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (Current() && inputRevision == mcpRevision)
                            { opened.McpResult.Text = $"Actual inert tool result: {result.ToolId}\nSource revision: {result.SourceRevision}\n"
                                + $"Observed UTC: {result.ObservedAt:O}\n{result.Text}\n{result.StructuredJson}"; }
                        });
                    });
                mcpRequest.Complete(HostOperationOutcome.Completed);
                if (Current())
                {
                    opened.Confirmation.Text = string.Empty;
                    opened.Status.Text = $"Retained exact MCP skill request {mcpReceipt.Message.Request.RequestId.Value:D}: "
                        + $"{mcpReceipt.Message.Outcome} / {mcpReceipt.Message.Reason}. "
                        + (mcpReceipt.Message.AssistantResponse?.Body ?? "No assistant answer. Inspect the exact conversation; no automatic replay.");
                }
                return;
            }
            var prepared = toolInvocation ?? throw new InvalidOperationException("Review this exact invocation before confirming.");
            toolInvocation = null;
            opened.Confirm.IsEnabled = false;
            var revision = inputRevision;
            using var request = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            var receipt = await service.InvokeAsync(prepared, () => Reviewed() && inputRevision == revision,
                async (proposal, generation, address, admitted, approvalToken) =>
                {
                    if (questions is null) { throw new InvalidOperationException("Native per-action page approval is unavailable."); }
                    var spec = new QuestionSpec("Exact page approval", QuestionKind.SingleChoice, [new("once", "Approve once")]);
                    var presentation = await questions.AskConversationAsync(opened, proposal.Request, generation,
                        spec, proposal.ExpiresAt, admitted, approvalToken, proposal, address);
                    return new(presentation.Presented, presentation.Completed);
                }, token);
            request.Complete(HostOperationOutcome.Completed);
            if (Current())
            {
                opened.Confirmation.Text = string.Empty;
                opened.Status.Text = $"Retained exact request {receipt.Message.Request.RequestId.Value:D}: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                    + (receipt.Message.AssistantResponse?.Body ?? "No assistant answer. Inspect the exact conversation; no automatic replay.");
            }
        }
        else { throw new InvalidOperationException("Use the explicit tool skill review, target, request, enable, review invocation or confirm invocation commands."); }
    }

    private void RetireToolReview()
    {
        toolReview?.Dispose();
        toolReview = null;
        toolInvocation?.Dispose();
        toolInvocation = null;
        toolScope = null;
        mcpInvocation?.Dispose();
        mcpInvocation = null;
    }

    private static string Describe(LocalWebPageScope scope) =>
        $"At most {(scope.MaximumCalls == 1 ? "ONE" : "TWO")} host WebPageGet call(s); {scope.MaximumRounds} model rounds; "
        + $"cumulative {ModelToolProtocol.MaximumInputUtf8Bytes} input / {ModelToolProtocol.MaximumOutputUtf8Bytes} output UTF-8 bytes; "
        + $"{LocalWebPageTool.Descriptor.Limits.MaximumOutputUtf8Bytes} bytes per inert page result; "
        + $"{ModelToolProtocol.Deadline.TotalSeconds:0}-second deadline.\n"
        + string.Join("\n", scope.Destinations.Select((address, index) => $"SOURCE {index + 1}: {address.AbsoluteUri}"))
        + "\nEach source needs its own current once-only action approval. No scripts or hosted disclosure.";
}
