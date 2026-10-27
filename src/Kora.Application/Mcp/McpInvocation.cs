using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Core.Network;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Mcp;

/// <summary>One session/origin/task-bound connection, exact selected tool, single call and joined transport lifetime.</summary>
internal sealed class McpInvocation(
    McpServerConfigurationService configuration, IMcpConnectionBroker broker, McpReadOnlyPolicy effects,
    IHostInteractionStore interactions, ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
    ISecurityAuditLog audit, TimeProvider time, HostRequest original, HostRevision generation,
    string serverId, McpAccountReference account, McpApprovalPresenter presenter, McpToolSelector selectTool,
    Func<bool> admitted) : IAsyncDisposable
{
    private readonly long control = access.ControlRevision;
    private readonly long useRevision = configuration.UseControlRevision;
    private readonly DateTimeOffset expiresAt = time.GetUtcNow().AddMinutes(2);
    private readonly HostRequest interactionRequest = new(original.RequestId, original.SessionId,
        original.TaskId, original.Origin, new(Guid.NewGuid()));
    private readonly List<Func<CancellationToken, Task>> grants = [];
    private McpConnectionDescriptor? descriptor;
    private IMcpConnection? connection;
    private McpDiscoveredTool? selected;
    private McpAccountChoice? accountChoice;
    private Dependencies.ModelTurn? turn;
    private HostTaskRecord? originalUserTask;
    private int used;
    private bool disposed;
    private bool connectionFaulted;
    internal bool Completed { get; private set; }
    internal McpModelResult? Result { get; private set; }
    internal McpModelTool ModelTool => selected is { } tool
        ? new("mcp." + tool.Name, tool.Schema.Json)
        : throw new InvalidOperationException("No exact MCP tool has been reviewed.");

    internal void BindTurn(Dependencies.ModelTurn value)
    {
        if (turn is not null || originalUserTask is not null || value.Provenance.Request != original)
        { throw new InvalidOperationException("MCP cannot be rebound to another original request or session."); }
        turn = value;
    }

    internal void BindOriginalUserTask(HostTaskRecord value)
    {
        if (turn is not null || originalUserTask is not null || value.Request != original
            || value.State != HostTaskState.DispatchRecorded || value.Revision.Value != 2)
        { throw new InvalidOperationException("Only the exact original-user dispatch can own this MCP invocation."); }
        originalUserTask = value;
    }

    internal async Task<CapabilityReply> InvokeOriginalUserAsync(McpArgumentsPresenter input, CancellationToken token)
    {
        if (originalUserTask is null || turn is not null)
        { return new(CapabilityOutcome.Denied, "original-mcp-dispatch-required"); }
        await ValidateAsync(token).ConfigureAwait(false);
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(interactionRequest, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var arguments = await input(interactionRequest, generation, expiresAt, ModelTool,
                () => Live(token), token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            if (arguments is null) { throw new OperationCanceledException("MCP input was not submitted.", token); }
            var reply = await InvokeSelectedAsync(ModelTool.Id, arguments, token).ConfigureAwait(false);
            activity.Complete(reply.Outcome == CapabilityOutcome.Succeeded
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return reply;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    internal async Task PrepareAsync(CancellationToken token)
    {
        if (connectionFaulted) { throw new InvalidOperationException("The failed MCP connection requires a new explicitly reviewed request."); }
        await ValidateAsync(token).ConfigureAwait(false);
        descriptor = await configuration.ResolveHttpsAsync(serverId, account, token).ConfigureAwait(false);
        var accounts = await broker.ReadAccountsAsync(token).ConfigureAwait(false);
        accountChoice = accounts.SingleOrDefault(choice => choice.Reference == account)
            ?? throw new InvalidOperationException("The explicitly selected delegated MCP account is not available.");
        await ValidateAsync(token).ConfigureAwait(false);
        await ApproveAsync(McpReviewStage.Connection, null, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        connection = await broker.ConnectAsync(account, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        var initialize = await ExchangeAsync(McpProtocol.InitializeRequest(), McpProtocol.MaximumDiscoveryUtf8Bytes, token).ConfigureAwait(false);
        McpProtocol.ValidateInitialization(initialize.AsSpan());
        await connection.NotifyAsync(ImmutableArray.CreateRange(McpProtocol.InitializedNotification()), token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        var discovery = await ExchangeAsync(McpProtocol.DiscoveryRequest(), McpProtocol.MaximumDiscoveryUtf8Bytes, token).ConfigureAwait(false);
        var tools = McpProtocol.ReadDiscovery(discovery.AsSpan());
        var eligible = tools.Where(tool => effects.Find(descriptor, tool) is not null).ToArray();
        if (eligible.Length == 0)
        { throw new InvalidOperationException("No discovered MCP tool has an exact current host-owned read-only effect mapping."); }
        var choice = await SelectToolAsync(eligible, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        selected = eligible.SingleOrDefault(tool => string.Equals(tool.Name, choice, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("An exact session MCP tool selection is required.");
        await ApproveAsync(McpReviewStage.ToolSelection, null, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
    }

    internal async ValueTask<CapabilityReply> InvokeAsync(ModelTurnProvenance provenance, string id,
        string arguments, CancellationToken token)
    {
        if (provenance.Request != original || provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || originalUserTask is not null || !string.Equals(id, ModelTool.Id, StringComparison.Ordinal))
        { return new(CapabilityOutcome.Denied, "exact-mcp-turn-not-admitted"); }
        return await InvokeSelectedAsync(id, arguments, token).ConfigureAwait(false);
    }

    private async ValueTask<CapabilityReply> InvokeSelectedAsync(string id, string arguments, CancellationToken token)
    {
        if (Interlocked.Exchange(ref used, 1) != 0)
        { return new(CapabilityOutcome.Denied, "exact-mcp-turn-not-admitted"); }
        selected!.Schema.ValidateArguments(arguments);
        Kora.Core.Conversation.ConversationText.Validate(arguments, submission: true);
        await ValidateAsync(token).ConfigureAwait(false);
        await ApproveAsync(McpReviewStage.Invocation, arguments, token).ConfigureAwait(false);
        ImmutableArray<byte> observed;
        try
        {
            observed = await ExchangeAsync(McpProtocol.InvocationRequest(selected.Name, selected.Schema, arguments),
                McpProtocol.MaximumResultUtf8Bytes, token).ConfigureAwait(false);
        }
        catch (McpExchangeException)
        { return new(CapabilityOutcome.Failed, "mcp-transport-failed-review-before-new-request"); }
        var result = McpProtocol.ReadInvocationResult(observed.AsSpan());
        await ValidateAsync(token).ConfigureAwait(false);
        if (result.IsError) { return new(CapabilityOutcome.Failed, "mcp-server-reported-error"); }
        Kora.Core.Conversation.ConversationText.RequireNoCredentials(result.Text);
        if (result.StructuredJson is { } structured)
        { Kora.Core.Conversation.ConversationText.RequireNoCredentials(structured); }
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed-inert-local-mcp-result",
            Mcp: new(id, descriptor!.SourceRevision, time.GetUtcNow(), result.Text, result.StructuredJson));
        if (!CapabilityReplyWire.IsWithinLimit(reply))
        { return new(CapabilityOutcome.Failed, "mcp-complete-result-exceeds-limit"); }
        Completed = true;
        Result = reply.Mcp;
        return reply;
    }

    private async Task<ImmutableArray<byte>> ExchangeAsync(byte[] wire, int limit, CancellationToken token)
    {
        await ValidateAsync(token).ConfigureAwait(false);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "mcp.exchange", SecurityAuditOutcome.Requested,
            original.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            original.SessionId.Value.ToString("D"));
        audit.Write(requested);
        try
        {
            ImmutableArray<byte> result;
            try
            {
                result = await connection!.ExchangeAsync(ImmutableArray.CreateRange(wire), limit, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!token.IsCancellationRequested
                && exception is HttpRequestException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                connectionFaulted = true;
                throw new McpExchangeException(exception);
            }
            await ValidateAsync(token).ConfigureAwait(false);
            if (result.IsDefaultOrEmpty || result.Length > limit)
            { throw new InvalidDataException("The complete MCP transport response is empty or exceeds its bound."); }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            await ValidateAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
            throw;
        }
    }

    internal async Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Live(token)) { throw new InvalidOperationException("The exact MCP session, source or permission admission is closed."); }
        var session = await workspace.ReadMetadataAsync(original.SessionId, token).ConfigureAwait(false);
        var task = await workspace.ReadTaskAsync(original.SessionId, original.TaskId, token).ConfigureAwait(false);
        if (!Live(token) || !session.Authority.IsActive || session.Authority.SessionId != original.SessionId
            || session.Authority.Generation != generation || task is null || !task.CurrentSource
            || task.Generation != generation || task.Task.Request != original
            || task.Task.Revision != (turn?.TaskRevision ?? originalUserTask?.Revision ?? new HostRevision(1))
            || task.Task.State is not (HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded))
        { throw new InvalidOperationException("The original MCP task/session authority is no longer current."); }
        if (descriptor is not null) { await configuration.ValidateCurrentAsync(descriptor, token).ConfigureAwait(false); }
        // A faulted connection can only finish without content; session, source and grants still require current authority.
        if (connection is not null && !connectionFaulted)
        {
            if (connection.Account != account || connection.Endpoint != account.Destination
                || connection.RemoteAddresses.Count == 0 || connection.RemoteAddresses.Any(address => !WebPageNetworkPolicy.IsPublic(address))
                || !await connection.IsCurrentAsync(token).ConfigureAwait(false))
            { throw new InvalidOperationException("The exact delegated account, HTTPS destination or public DNS binding is unavailable."); }
        }
        foreach (var validate in grants) { await validate(token).ConfigureAwait(false); }
        if (!Live(token)) { throw new InvalidOperationException("MCP admission changed during validation."); }
    }

    private bool Live(CancellationToken token) => !disposed && !token.IsCancellationRequested
        && access.CanControl && access.ControlRevision == control && configuration.UseControlRevision == useRevision
        && admitted() && time.GetUtcNow() < expiresAt
        && HostActivity.Current is { } current && current.Request.IsWithinIntent(original)
        && current.Outcome == HostOperationOutcome.Unknown && !current.Activity!.IsStopped;

    private async Task<string?> SelectToolAsync(IReadOnlyList<McpDiscoveredTool> tools, CancellationToken token)
    {
        var request = interactionRequest;
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var choice = await selectTool(request, generation, expiresAt, tools, () => Live(token), token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return choice;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private async Task ApproveAsync(McpReviewStage stage, string? arguments, CancellationToken token)
    {
        var invocation = interactionRequest;
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var source = descriptor!;
            var reviewSource = originalUserTask is null ? null : new McpReviewSource(source.ServerName, source.SourcePath, source.SourceRevision);
            var binding = new ExactOperationBinding("mcp.read-only", "mcp", "https-read-only",
                Hash(selected?.Schema.Json ?? "connection"), reviewSource is null ? source.SourceRevision : Hash(JsonSerializer.Serialize(reviewSource)),
                source.SourceRevision,
                Hash(typeof(McpInvocation).Module.ModuleVersionId.ToString("D")),
                Hash(JsonSerializer.Serialize(new { invocation, stage, arguments })),
                Hash(selected?.Name ?? "connection"), Hash(JsonSerializer.Serialize(account)),
                account.Destination.Digest, Hash("inert-local-only-result-v1"), new(1));
            var proposal = new HostOperationProposal(invocation, new(Guid.NewGuid()), new(1), binding,
                HostOperationEffect.BoundedRead, expiresAt);
            var review = new McpReview(stage, source.Endpoint.Address, account, accountChoice!.DisplayName,
                selected?.Name, selected?.Schema.Json, arguments) { Source = reviewSource };
            var presented = await presenter(proposal, generation, review, () => Live(token), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Live(token) || presented.Presented.Proposal != proposal || presented.Presented.Key.Request != invocation
                || presented.Presented.SessionGeneration != generation
                || presented.Completed is not { Outcome: HostInteractionOutcome.Approved, Grant: { } grant }
                || grant.ApprovedProposal != proposal || grant.Scope != OperationGrantScope.Once || grant.SessionGeneration != generation
                || presented.Completed.Question is not { Status: QuestionStatus.Answered } answer
                || answer.Key.QuestionId != presented.Presented.Key.QuestionId || answer.Key.Request != invocation
                || answer.Proposal != proposal
                || answer.Draft is not { Text: null } draft || !draft.Choices.SequenceEqual(["once"])
                || answer.AnswerChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                || stage == McpReviewStage.Connection && answer.AnswerChannel != RequestOrigin.LocalUi)
            { throw new InvalidOperationException("Exact native MCP endpoint/account/tool/argument approval is required."); }
            var authorization = new HostAuthorizationService(interactions, time);
            var consumed = await authorization.ConsumeAsync(invocation, grant.Id, grant.Revision, token).ConfigureAwait(false);
            if (consumed.Outcome != HostInteractionOutcome.Consumed || consumed.Grant is not { } receipt
                || consumed.Admission is not { } admission)
            { throw new InvalidOperationException("The exact MCP approval is no longer current."); }
            var dispatched = await authorization.DispatchAsync(invocation, admission, token).ConfigureAwait(false);
            if (dispatched.Outcome != HostInteractionOutcome.Consumed || dispatched.Admission is not { } dispatch)
            { throw new InvalidOperationException("The exact MCP policy admission was retired before dispatch."); }
            grants.Add(async checkToken =>
            {
                var current = HostActivity.RequireCurrent();
                using var validation = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Policy,
                    [new ActivityLink(current.Activity!.Context)]);
                try
                {
                    var decision = await new InteractionTransaction(interactions).RunAsync(invocation, "approval.mcp-revalidate", snapshot =>
                        Live(checkToken) && snapshot.Session.Generation == generation && InteractionTransaction.CanInteract(snapshot)
                            && snapshot.Policy.OtherMandatoryGatesSatisfied
                            && snapshot.EffectivePermission == dispatch.Policy && snapshot.Admissions.Contains(dispatch)
                            && snapshot.Grants.SingleOrDefault(item => item.Id == receipt.Id) == receipt
                            && receipt.Status == OperationGrantStatus.Consumed
                            ? (snapshot, new(HostInteractionOutcome.Approved, "exact-mcp-use-current"))
                            : InteractionTransaction.Reject(snapshot, "exact-mcp-use-retired"), checkToken).ConfigureAwait(false);
                    if (decision.Outcome != HostInteractionOutcome.Approved || !Live(checkToken))
                    { throw new InvalidOperationException("The exact MCP approval was revoked, expired or retired."); }
                    validation.Complete(HostOperationOutcome.Completed);
                }
                catch (OperationCanceledException) { validation.Complete(HostOperationOutcome.Cancelled); throw; }
                catch { validation.Complete(HostOperationOutcome.Failed); throw; }
            });
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await JoinTransportAsync().ConfigureAwait(false);
    }

    internal async ValueTask JoinTransportAsync()
    {
        if (connection is { } owned)
        {
            connection = null;
            await owned.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async Task ValidateResultSourceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Live(token)) { throw new InvalidOperationException("The original MCP result presentation was retired."); }
        await configuration.ValidateCurrentAsync(descriptor!, token).ConfigureAwait(false);
        if (!Live(token)) { throw new InvalidOperationException("The original MCP result source was retired."); }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class McpExchangeException(Exception innerException)
        : IOException("The MCP exchange failed. Review the original request before explicitly starting a new connection.", innerException);
}
