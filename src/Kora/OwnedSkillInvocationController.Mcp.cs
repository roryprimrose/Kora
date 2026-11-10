using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora;

internal sealed partial class OwnedSkillInvocationController
{
    private SharedSkillInvocationView? mcpWindow;
    private SharedSkillInvocationWindow? mcpAdapter;
    private ReviewedMcpSkillInvocation? mcpInvocation;
    internal bool IsMcpOpen => mcpWindow is not null;

    private void OpenMcp(OwnedSkillUseReview source)
    {
        void RunAction(string action) => Start(action switch
        {
            "Enable" => "enable", "Targets" => "targets", "NextTargets" => "next-targets",
            "McpOptions" => "mcp-options", "ReviewToolRevision" => "review-tool", "Prepare" => "review-use",
            "Invoke" => "invoke", "Close" => "close-use",
            _ => throw new InvalidOperationException("Unsupported owned MCP operation."),
        });
        SharedSkillInvocationView opened;
        if (getWorkbench is not null && presentMcpWorkbench is not null)
        { opened = new(getWorkbench(), RunAction, () => main.CanRevealPrivatePresentation && !disposed, toolCapable: true, mcp: true, owned: true); }
        else
        {
            mcpAdapter = new(RunAction, toolCapable: true, mcp: true, owned: true);
            opened = mcpAdapter.View;
        }
        mcpWindow = opened;
        opened.Instructions.Text = source.Text;
        opened.Request.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.TextBox.TextProperty)
            { InvalidateMcp(); requestAuthority = new(RequestOrigin.LocalUi, Eligible); }
        };
        opened.McpArguments.PropertyChanged += (_, args) =>
        { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { InvalidateMcp(); } };
        opened.Targets.SelectionChanged += (_, _) => InvalidateMcp();
        opened.McpServers.SelectionChanged += (_, _) => InvalidateMcp();
        opened.McpAccounts.SelectionChanged += (_, _) => InvalidateMcp();
        opened.Closed += (_, _) => Retire();
        if (getWorkbench is not null) { opened.PresentationLost += OnPresentationLost; }
        if (presentMcpWorkbench is not null) { presentMcpWorkbench(opened); }
        else { mcpAdapter!.Show(); }
        if (!Eligible()) { Retire(); return; }
        opened.Status.Text = "Exact disabled owned package read. Select a target and account, review the revision, then separately Enable. Nothing connected.";
    }

    private async Task RunOwnedMcp(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority, Func<bool> sourceEligible)
    {
        var opened = mcpWindow ?? throw new InvalidOperationException("The owned MCP review is closed.");
        var source = review ?? throw new InvalidOperationException("Read the exact owned source first.");
        var service = RequireToolWorkflow();
        bool Current() => Eligible() && authority.IsCurrent() && sourceEligible() && ReferenceEquals(mcpWindow, opened);
        bool SourceCurrent() => Current() && source.IsCurrent;
        if (!SourceCurrent()) { throw new InvalidOperationException("The owned MCP source or original authority expired."); }
        var token = lifetime!.Token;
        switch (command.Operation)
        {
            case "mcp-options":
                InvalidateMcp();
                opened.McpServers.ItemsSource = null;
                opened.McpAccounts.ItemsSource = null;
                var servers = service.ReadMcpServers();
                var accounts = await service.ReadMcpAccountsAsync(token);
                if (!SourceCurrent()) { throw new OperationCanceledException("MCP selection changed."); }
                opened.McpServers.ItemsSource = servers;
                opened.McpAccounts.ItemsSource = accounts;
                opened.Status.Text = "Select an existing included server and delegated account. No credentials acquired or connection opened.";
                break;
            case "targets":
            case "next-targets":
                InvalidateMcp();
                opened.Targets.ItemsSource = null;
                var page = await workflow.ReadTargetsAsync(command.Operation is "next-targets"
                    ? next ?? throw new InvalidOperationException("No next page. Refresh targets.") : null, authority, SourceCurrent, token);
                if (!SourceCurrent()) { throw new OperationCanceledException("Target admission changed."); }
                next = page.Next;
                opened.Targets.ItemsSource = page.Records.Where(row => row.Authority.IsActive).Select(row => new SharedSkillConversationTarget(row)).ToArray();
                break;
            case "target":
                opened.Targets.SelectedItem = (opened.Targets.ItemsSource as SharedSkillConversationTarget[] ?? [])
                    .SingleOrDefault(row => string.Equals(row.Entry.Authority.SessionId.Value.ToString("D"), command.Literal, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Select an exact displayed Active conversation.");
                break;
            case "mcp-server":
                opened.McpServers.SelectedItem = (opened.McpServers.ItemsSource as IReadOnlyList<McpServerConfigurationSummary> ?? [])
                    .SingleOrDefault(row => string.Equals(row.Id, command.Literal, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Select an exact displayed included server.");
                break;
            case "mcp-account":
                opened.McpAccounts.SelectedItem = (opened.McpAccounts.ItemsSource as IReadOnlyList<McpAccountChoice> ?? [])
                    .SingleOrDefault(row => string.Equals(row.Reference.AccountId.ToString("D"), command.Literal, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Select an exact displayed delegated account.");
                break;
            case "request":
                opened.Request.Text = command.Literal;
                requestAuthority = authority;
                break;
            case "mcp-arguments":
                opened.McpArguments.Text = command.Literal;
                break;
            case "review-tool":
                if (toolReview is not null) { throw new InvalidOperationException("Start a fresh source review instead of renewing enablement."); }
                var target = (opened.Targets.SelectedItem as SharedSkillConversationTarget)?.Entry
                    ?? throw new InvalidOperationException("Select one exact Active target.");
                _ = opened.McpServers.SelectedItem as McpServerConfigurationSummary
                    ?? throw new InvalidOperationException("Select an included server.");
                _ = opened.McpAccounts.SelectedItem as McpAccountChoice
                    ?? throw new InvalidOperationException("Select a delegated account.");
                var input = inputRevision;
                toolReview = await service.ReviewOwnedAsync(workflow, source, target, authority,
                    () => SourceCurrent() && inputRevision == input, token);
                opened.Status.Text = "Owned source and target reviewed. Separately Enable; schema, mapping and outgoing approvals remain mandatory.";
                break;
            case "enable":
                await service.EnableAsync(toolReview ?? throw new InvalidOperationException("Review the exact tool revision first."),
                    SourceCurrent, authority.Origin, token);
                opened.Status.Text = "Exact owned revision enabled once in memory. No connection or invocation permission granted.";
                break;
            case "review-use":
                var captured = toolReview ?? throw new InvalidOperationException("Review and enable the exact owned revision first.");
                var originalInput = requestAuthority ?? authority;
                mcpInvocation?.Dispose();
                var revision = inputRevision;
                mcpInvocation = await McpSkillInvocationPresenter.PrepareAsync(service, captured, opened,
                    () => SourceCurrent() && inputRevision == revision, originalInput.Origin, token);
                break;
            case "invoke":
                var prepared = mcpInvocation ?? throw new InvalidOperationException("Review the exact invocation first.");
                if (prepared.Review.Origin != authority.Origin) { throw new InvalidOperationException("Preserve the reviewed original request channel."); }
                mcpInvocation = null;
                var invocationRevision = inputRevision;
                McpModelResult? completedResult = null;
                invoking = true;
                var receipt = await McpSkillInvocationPresenter.InvokeAsync(service, prepared, opened,
                    questions ?? throw new InvalidOperationException("Native MCP review is unavailable."),
                    () => SourceCurrent() && inputRevision == invocationRevision, token,
                    (_, result) =>
                    {
                        if (Current() && inputRevision == invocationRevision) { completedResult = result; }
                    });
                if (Current() && operationPresentation?.Invoke() != false && inputRevision == invocationRevision)
                {
                    if (completedResult is { } result)
                    { opened.McpResult.Text = $"Actual inert result: {result.ToolId}\nSource: {result.SourceRevision}\n{result.Text}\n{result.StructuredJson}"; }
                    opened.Confirmation.Text = "";
                    opened.Status.Text = $"Retained exact request {receipt.Message.Request.RequestId.Value:D}: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                        + (receipt.Message.AssistantResponse?.Body ?? "No answer. Inspect the conversation; no automatic retry.");
                }
                break;
            default: throw new InvalidOperationException(OwnedSkillAuthoringCommand.Syntax);
        }
    }

    private void InvalidateMcp()
    {
        Interlocked.Increment(ref inputRevision);
        if (toolReview is not null) { toolReview.Dispose(); toolReview = null; }
        mcpInvocation?.Dispose();
        mcpInvocation = null;
        if (mcpWindow is { } opened) { opened.Confirmation.Text = ""; opened.McpResult.Text = ""; opened.Confirm.IsEnabled = false; }
    }

    private void RetireMcp()
    {
        var opened = mcpWindow;
        mcpWindow = null;
        mcpAdapter = null;
        mcpInvocation?.Dispose();
        mcpInvocation = null;
        opened?.Clear();
        opened?.Close();
    }
}
