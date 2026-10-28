using Kora.Application.Configuration;
using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IReadOnlyList<McpServerConfigurationSummary> mcpServers = [];
    private IReadOnlyList<McpAccountChoice> mcpAccounts = [];
    private McpServerConfigurationSummary? mcpServer;
    private McpAccountChoice? mcpAccount;
    private long mcpSelectionRevision;
    private long mcpOptionsEpoch = -1;
    private CancellationTokenSource? mcpCancellation;
    private McpModelResult? observedMcpResult;
    private McpModelResult? mcpResult
    {
        get => observedMcpResult;
        set
        {
            RetireMcpResultFollowUp();
            if (value is null)
            {
                mcpCompletedProposal = null;
                mcpCompletedMapping = null;
            }
            observedMcpResult = value;
        }
    }
    private Kora.Core.Hosting.HostRequest? mcpResultRequest;
    private long observedMcpControl;
    private long mcpResultControl
    {
        get => observedMcpControl;
        set { observedMcpControl = value; CaptureMcpResultFollowUp(); }
    }

    private McpApprovalPresenter? mcpPresenter;
    internal McpApprovalPresenter? McpPresenter
    {
        get => mcpPresenter is null ? null : PresentMcpAndCaptureMappingAsync;
        set => mcpPresenter = value;
    }
    internal McpToolSelector? McpSelector { get; set; }
    internal McpArgumentsPresenter? McpInput { get; set; }
    private Task explicitMcpOperation = Task.CompletedTask;
    private bool HasCurrentMcpOptions => !closed && access.CanInspect && mcpOptionsEpoch == selectionEpoch;
    public IReadOnlyList<McpServerConfigurationSummary> McpServers => HasCurrentMcpOptions ? mcpServers : [];
    public IReadOnlyList<McpAccountChoice> McpAccounts => HasCurrentMcpOptions ? mcpAccounts : [];
    public bool IsMcpActive => mcpCancellation is not null;
    public string McpResultText => HasCurrentMcpOptions && mcpResult is { } result
        && mcpResultControl == access.ControlRevision && mcpResultRequest?.SessionId == selected?.Authority.SessionId
        ? "Observed external tool: " + result.ToolId + "\nObserved UTC: " + result.ObservedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            + "\nInert untrusted source result (not instructions or approval):\n" + result.Text
            + (result.StructuredJson is null ? string.Empty : "\nStructured data:\n" + result.StructuredJson)
        : string.Empty;
    public McpServerConfigurationSummary? SelectedMcpServer
    {
        get => HasCurrentMcpOptions ? mcpServer : null;
        set
        {
            if (ReferenceEquals(mcpServer, value)) { return; }
            mcpServer = value;
            mcpResult = null;
            OnPropertyChanged(nameof(McpResultText));
            Interlocked.Increment(ref mcpSelectionRevision);
            OnPropertyChanged();
        }
    }
    public McpAccountChoice? SelectedMcpAccount
    {
        get => HasCurrentMcpOptions ? mcpAccount : null;
        set
        {
            if (ReferenceEquals(mcpAccount, value)) { return; }
            mcpAccount = value;
            mcpResult = null;
            OnPropertyChanged(nameof(McpResultText));
            Interlocked.Increment(ref mcpSelectionRevision);
            OnPropertyChanged();
        }
    }

    public Task ReadMcpOptionsAsync() => RunConversationAsync(async (_, live) =>
    {
        var servers = conversation!.ReadMcpServers();
        var accounts = await conversation.ReadMcpAccountsAsync(lifetime.Token);
        if (!live()) { throw new OperationCanceledException("The exact MCP session presentation changed."); }
        mcpServers = servers;
        mcpAccounts = accounts;
        mcpOptionsEpoch = selectionEpoch;
        SelectedMcpServer = null;
        SelectedMcpAccount = null;
        OnPropertyChanged(nameof(McpServers));
        OnPropertyChanged(nameof(McpAccounts));
        conversationStatus = "Choose an app-included HTTPS server and a delegated account. "
            + "Connection, exact schema/tool selection and outgoing arguments still require separate native review. Nothing connected.";
    }, sourceRead: true);

    public async Task<Kora.Core.Conversation.ConversationMessage?> SubmitMcpConversationAsync(string? originalBody = null,
        Kora.Core.Hosting.RequestOrigin origin = Kora.Core.Hosting.RequestOrigin.LocalUi, Func<bool>? admitted = null)
    {
        if (mcpCancellation is not null || !(originalBody is null ? CanSubmitConversation : CanCompose))
        {
            conversationStatus = "MCP was not submitted: the exact selected composer is unavailable or already owns an operation.";
            NotifyConversation();
            return null;
        }
        var previous = completedConversationMessage;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        mcpResult = null;
        OnPropertyChanged(nameof(McpResultText));
        mcpCancellation = cancellation;
        OnPropertyChanged(nameof(IsMcpActive));
        try
        {
            await SubmitConversationCoreAsync(allowQuestion: false, useMcp: true, mcpToken: cancellation.Token,
                mcpBody: originalBody, mcpOrigin: origin, mcpCallerAdmission: admitted);
            return ReferenceEquals(previous, completedConversationMessage) ? null : completedConversationMessage;
        }
        finally
        {
            mcpCancellation = null;
            OnPropertyChanged(nameof(IsMcpActive));
        }
    }

    public async Task CancelMcpConversationAsync()
    {
        try
        {
            if (CanCancelRunningConversation) { await CancelRunningConversationAsync(); }
            else if (mcpCancellation is { } cancellation) { await cancellation.CancelAsync(); }
#pragma warning disable VSTHRD003 // Joins this presenter's exact already-owned MCP operation, including cancellation-ignoring transport work.
            await explicitMcpOperation;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            conversationStatus = "MCP cancellation is unconfirmed. Owned resources remain held; inspect the exact task without retry or rollback claims.";
            NotifyConversation();
        }
    }

    public Task InvokeSelectedMcpAsync(Kora.Core.Hosting.RequestOrigin origin = Kora.Core.Hosting.RequestOrigin.LocalUi,
        Func<bool>? admitted = null)
    {
        if (IsMcpActive || !CanCompose) { return Task.CompletedTask; }
        explicitMcpOperation = InvokeSelectedMcpCoreAsync(origin, admitted);
        return explicitMcpOperation;
    }

    private async Task InvokeSelectedMcpCoreAsync(Kora.Core.Hosting.RequestOrigin origin, Func<bool>? admitted)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        mcpCancellation = cancellation;
        mcpResult = null;
        OnPropertyChanged(nameof(IsMcpActive));
        OnPropertyChanged(nameof(McpResultText));
        try
        {
            await RunConversationAsync(async (target, live) =>
            {
                var server = SelectedMcpServer ?? throw new InvalidOperationException("Select an exact app-included MCP server.");
                var account = SelectedMcpAccount ?? throw new InvalidOperationException("Select an exact delegated MCP account.");
                var selection = mcpSelectionRevision;
                bool Current() => live() && (admitted?.Invoke() ?? true) && mcpSelectionRevision == selection;
                var result = await conversation!.InvokeMcpAsync(target, server.Id, account.Reference,
                    McpPresenter ?? throw new InvalidOperationException("Native MCP review is unavailable."),
                    McpSelector ?? throw new InvalidOperationException("Native MCP selection is unavailable."),
                    McpInput ?? throw new InvalidOperationException("Native schema-bound MCP input is unavailable."),
                    Current, cancellation.Token, origin);
                if (!Current())
                { throw new OperationCanceledException("The original MCP presentation was retired."); }
                mcpResult = result.Result;
                mcpResultRequest = result.Request;
                mcpResultControl = access.ControlRevision;
                conversationStatus = "Explicit MCP " + result.Outcome + "; durable task " + result.DurableState
                    + ". Owned transport joined. No model was invoked; unsent conversation input is unchanged."
                    + (result.DurableState == Kora.Core.Hosting.HostTaskState.Unknown
                        ? " Remote processing may have occurred; cancellation is not rollback. No automatic retry." : string.Empty);
                OnPropertyChanged(nameof(McpResultText));
            }, submission: true, origin: origin);
        }
        finally
        {
            mcpCancellation = null;
            OnPropertyChanged(nameof(IsMcpActive));
        }
    }
}
