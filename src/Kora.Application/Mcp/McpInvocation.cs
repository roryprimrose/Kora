using System.Collections.Immutable;
using System.Diagnostics;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;
using IMcpProcessPreparation = Kora.Core.Mcp.IMcpConnectionBroker.IMcpProcessPreparation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Context;
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
internal sealed partial class McpInvocation(
    McpServerConfigurationService configuration, IMcpConnectionBroker broker, McpReadOnlyPolicy effects,
    IHostInteractionStore interactions, ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
    ISecurityAuditLog audit, TimeProvider time, HostRequest original, HostRevision generation,
    string serverId, McpConnectionIdentity identity, McpApprovalPresenter presenter, McpToolSelector? selectTool,
    Func<bool> admitted, McpSkillBinding? skill = null,
    Func<CancellationToken, Task>? validateSource = null) : IAsyncDisposable
{
    private readonly long control = access.ControlRevision;
    private readonly long useRevision = configuration.UseControlRevision;
    private readonly string? capabilityRevision = configuration.CaptureCapabilityRevision(serverId);
    private readonly DateTimeOffset expiresAt = time.GetUtcNow().AddMinutes(2);
    private readonly HostRequest interactionRequest = new(original.RequestId, original.SessionId,
        original.TaskId, original.Origin, new(Guid.NewGuid()));
    private readonly List<Func<CancellationToken, Task>> grants = [];
    private Kora.Core.Authorization.SessionPermission.SessionPermissionAdmission? initialAutomaticDisclosureAdmission;
    private Action<Kora.Core.Authorization.SessionPermission.SessionPermissionAdmission>? updateAutomaticDisclosureAdmission;
    private McpConnectionDescriptor? descriptor;
    private IMcpConnection? connection;
    private Task? transportJoin;
    private McpDiscoveredTool? selected;
    private McpAccountChoice? accountChoice;
    private string AccountDisplayName => accountChoice?.DisplayName ?? "Explicit local stdio profile (no delegated account)";
    private McpProcessAuthorization? processAuthorization;
    private IMcpProcessPreparation? preparedProcess;
    private Dependencies.ModelTurn? turn;
    private HostTaskRecord? originalUserTask;
    private McpContinuationReview? resultContinuation;
    private int used;
    private bool disposed;
    private bool connectionFaulted;
    private bool toolPrepared;
    private readonly Lock guidanceClockGate = new();
    private DateTimeOffset guidanceObservedAt = time.GetUtcNow();
    private bool guidanceClockRetired;
    private McpReadOnlyPolicy.DescriptorAdmission? guidanceDescriptor;
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
                () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
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
        await PrepareConnectionAsync(token, McpCapabilityCategory.Tools).ConfigureAwait(false);
        var inventory = await DiscoverKindAsync(McpCapabilityKind.Tool, token).ConfigureAwait(false);
        var eligible = inventory.Where(item => item.UnavailableReason is null
                && configuration.IsCapabilityEnabled(serverId, item.Kind, item.Id))
            .Select(item =>
            {
                using var definition = JsonDocument.Parse(item.DefinitionJson);
                return new McpDiscoveredTool(item.Id, new(definition.RootElement.GetProperty("inputSchema").GetRawText()));
            }).Where(tool => effects.Find(Source, tool) is not null
                && configuration.IsToolEnabled(serverId, tool.Name)).ToArray();
        if (eligible.Length == 0)
        { throw new InvalidOperationException("No discovered MCP tool has an exact current host-owned read-only effect mapping."); }
        var choice = await SelectToolAsync(eligible, token).ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        selected = eligible.SingleOrDefault(tool => string.Equals(tool.Name, choice, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("An exact session MCP tool selection is required.");
        if (skill is { } reviewed)
        {
            if (!string.Equals(selected.Name, reviewed.Reference.ToolName, StringComparison.Ordinal)
                || !string.Equals(selected.Schema.Digest, reviewed.Reference.SchemaDigest, StringComparison.Ordinal))
            { throw new InvalidOperationException("The independently selected current tool and schema do not match this skill revision's exact dependency."); }
            selected.Schema.ValidateArguments(reviewed.Arguments);
        }
        await ApproveAsync(McpReviewStage.ToolSelection, null, token).ConfigureAwait(false);
        toolPrepared = true;
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
        if (skill is { } binding && !string.Equals(arguments, binding.Arguments, StringComparison.Ordinal))
        { return new(CapabilityOutcome.Denied, "exact-original-mcp-skill-arguments-required"); }
        selected!.Schema.ValidateArguments(arguments);
        Kora.Core.Conversation.ConversationText.Validate(arguments, submission: true);
        await ValidateAsync(token).ConfigureAwait(false);
        await ApproveAsync(McpReviewStage.Invocation, arguments, token).ConfigureAwait(false);
        McpConnectionReply<McpRemoteResult> observed;
        try
        {
            observed = await ExchangeAsync((transport, current) => transport.InvokeAsync(selected.Name, selected.Schema, arguments, current),
                McpProtocol.MaximumResultUtf8Bytes, token).ConfigureAwait(false);
        }
        catch (McpExchangeException)
        { return new(CapabilityOutcome.Failed, "mcp-transport-failed-review-before-new-request"); }
        var result = observed.Value;
        await ValidateAsync(token).ConfigureAwait(false);
        if (result.IsError) { return new(CapabilityOutcome.Failed, "mcp-server-reported-error"); }
        Kora.Core.Conversation.ConversationText.RequireNoCredentials(result.Text);
        if (result.StructuredJson is { } structured)
        { Kora.Core.Conversation.ConversationText.RequireNoCredentials(structured); }
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed-inert-local-mcp-result",
            Mcp: new(id, descriptor!.SourceRevision, time.GetUtcNow(), result.Text, result.StructuredJson));
        if (!CapabilityReplyWire.IsWithinLimit(reply))
        { return new(CapabilityOutcome.Failed, "mcp-complete-result-exceeds-limit"); }
        await JoinTransportAsync().ConfigureAwait(false);
        await ValidateAsync(token).ConfigureAwait(false);
        if (turn is { } modelTurn)
        {
            var source = descriptor!;
            var observedResult = reply.Mcp!;
            var risk = LocalSourceRiskPolicy.Evaluate([modelTurn.Context.UserRequest, observedResult.Text,
                observedResult.StructuredJson ?? string.Empty, source.ServerName, source.Identity.NativeDestination,
                AccountDisplayName, selected.Schema.Json], token);
            if (risk != LocalSourceRiskOutcome.AdmittedPrivateLocalOnly)
            {
                return risk == LocalSourceRiskOutcome.Unavailable
                    ? new(CapabilityOutcome.Unavailable, "mcp-result-source-use-unavailable")
                    : new(CapabilityOutcome.Denied, "mcp-result-source-use-denied");
            }
            var target = await workspace.ReadMetadataAsync(original.SessionId, token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            if (target != modelTurn.ConversationTarget)
            { throw new InvalidOperationException("The original MCP conversation changed before complete result review."); }
            resultContinuation = new McpContinuationReview(observedResult, modelTurn.Context.UserRequest,
                target.Metadata?.Name.Value ?? "Unnamed conversation", original.Origin, LocalSourceRiskPolicy.Revision);
            if (!await ApproveAsync(McpReviewStage.ResultContinuation, arguments, token, resultContinuation).ConfigureAwait(false))
            { return new(CapabilityOutcome.Denied, "mcp-result-continuation-not-consented"); }
            await ValidateAsync(token).ConfigureAwait(false);
        }
        Completed = true;
        Result = reply.Mcp;
        return reply;
    }

    private async Task<McpConnectionReply<T>> ExchangeAsync<T>(
        Func<IMcpConnection, CancellationToken, Task<McpConnectionReply<T>>> invoke, int limit, CancellationToken token)
    {
        await ValidateAsync(token).ConfigureAwait(false);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "mcp.exchange", SecurityAuditOutcome.Requested,
            original.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            original.SessionId.Value.ToString("D"));
        audit.Write(requested);
        try
        {
            McpConnectionReply<T> result;
            try
            {
                await ValidateAsync(token).ConfigureAwait(false);
                result = await invoke(connection!, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!token.IsCancellationRequested
                && exception is HttpRequestException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                connectionFaulted = true;
                throw new McpExchangeException(exception);
            }
            await ValidateAsync(token).ConfigureAwait(false);
            if (result.Utf8Bytes < 1 || result.Utf8Bytes > limit)
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
        guidanceDescriptor?.Retire();
        _ = GuidanceClockCurrent();
        token.ThrowIfCancellationRequested();
        if (!Live(token)) { throw new InvalidOperationException("The exact MCP session, source or permission admission is closed."); }
        if (selected is not null && !configuration.IsToolEnabled(serverId, selected.Name))
        { throw new InvalidOperationException("The selected MCP tool is disabled."); }
        var session = await workspace.ReadMetadataAsync(original.SessionId, token).ConfigureAwait(false);
        var task = await workspace.ReadTaskAsync(original.SessionId, original.TaskId, token).ConfigureAwait(false);
        if (!Live(token) || !session.Authority.IsActive || session.Authority.SessionId != original.SessionId
            || session.Authority.Generation != generation || task is null || !task.CurrentSource
            || task.Generation != generation || task.Task.Request != original
            || task.Task.Revision != (turn?.TaskRevision ?? originalUserTask?.Revision ?? new HostRevision(1))
            || task.Task.State is not (HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded))
        { throw new InvalidOperationException("The original MCP task/session authority is no longer current."); }
        if (descriptor is not null) { await configuration.ValidateCurrentAsync(descriptor, token).ConfigureAwait(false); }
        if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
        if (identity.Account is { } delegated && (resultContinuation is not null || contentContinuation is not null)
            && !(await broker.ReadAccountsAsync(token).ConfigureAwait(false)).Any(choice => choice.Reference == delegated))
        { throw new InvalidOperationException("The separately reviewed result's delegated account is no longer available."); }
        // A faulted connection can only finish without content; session, source and grants still require current authority.
        if (connection is not null && !connectionFaulted)
        {
            if (connection.Identity != identity
                || identity.Account is { } account && (connection.Account != account || connection.Endpoint != account.Destination
                    || connection.RemoteAddresses.Count == 0
                    || connection.RemoteAddresses.Any(address => !WebPageNetworkPolicy.IsPublic(address)))
                || !await connection.IsCurrentAsync(token).ConfigureAwait(false))
            { throw new InvalidOperationException("The exact delegated account, HTTPS destination or public DNS binding is unavailable."); }
        }
        foreach (var validate in grants) { await validate(token).ConfigureAwait(false); }
        if (transportJoin is not null) { await ValidateAccountAsync(token).ConfigureAwait(false); }
        if (!Live(token)) { throw new InvalidOperationException("MCP admission changed during validation."); }
        if (toolPrepared && selected is not null && used == 0 && !connectionFaulted && GuidanceClockCurrent()
            && connection is not null && transportJoin is null)
        {
            var observation = configuration.CaptureDescriptorObservation();
            var withdrawal = connection.AdmissionWithdrawn;
            if (observation is not null && withdrawal.CanBeCanceled && !withdrawal.IsCancellationRequested)
            {
                bool DescriptorCurrent() => !disposed && !connectionFaulted && connection is not null
                    && used == 0 && transportJoin is null
                    && !token.IsCancellationRequested && !withdrawal.IsCancellationRequested
                    && access.CanControl && access.ControlRevision == control && admitted() && observation()
                    && GuidanceClockCurrent();
                guidanceDescriptor = effects.ObservePreparedDescriptor(Source, selected, DescriptorCurrent, time, expiresAt);
            }
        }
    }

    private bool GuidanceClockCurrent()
    {
        lock (guidanceClockGate)
        {
            var now = time.GetUtcNow();
            if (guidanceClockRetired || now < guidanceObservedAt || now >= expiresAt)
            { guidanceClockRetired = true; return false; }
            guidanceObservedAt = now;
            return true;
        }
    }

    private bool Admitted(CancellationToken token) => !disposed && !token.IsCancellationRequested
        && access.CanControl && access.ControlRevision == control && configuration.UseControlRevision == useRevision
        && string.Equals(configuration.CaptureCapabilityRevision(serverId), capabilityRevision, StringComparison.Ordinal)
        && (descriptor is null || configuration.SourceControlRevision(serverId) == descriptor.SourceControlRevision)
        && admitted() && time.GetUtcNow() < expiresAt
        && (skill is null || time.GetUtcNow() < skill.ExpiresAt);

    private bool Live(CancellationToken token) => Admitted(token)
        && HostActivity.Current is { } current && current.Request.IsWithinIntent(original)
        && current.Outcome == HostOperationOutcome.Unknown && !current.Activity!.IsStopped;

    private async Task<string> SelectToolAsync(IReadOnlyList<McpDiscoveredTool> tools, CancellationToken token)
    {
        if (selectTool is null) { throw new InvalidOperationException("This MCP operation has no tool-selection authority."); }
        var request = interactionRequest;
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var choice = await selectTool(request, generation, expiresAt, tools,
                () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
            if (choice is null)
            {
                await ValidateAsync(token).ConfigureAwait(false);
                throw new McpSelectionCancelledException(token);
            }
            activity.Complete(HostOperationOutcome.Completed);
            return choice;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private async Task<bool> ApproveAsync(McpReviewStage stage, string? arguments, CancellationToken token,
        McpContinuationReview? continuation = null, McpContentContinuationReview? contentReview = null,
        string? processAction = null, Action<Func<CancellationToken, Task>>? captureAuthority = null)
    {
        var invocation = interactionRequest;
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var source = descriptor!;
            var reviewSource = originalUserTask is null && skill is null && continuation is null && contentCapability is null
                ? null : new McpReviewSource(source.ServerName, source.SourcePath, source.SourceRevision);
            var review = new McpReview(stage, identity, AccountDisplayName,
                selected?.Name ?? contentCapability?.Id, selected?.Schema.Json ?? contentCapability?.DefinitionJson, arguments)
            {
                Source = reviewSource,
                Skill = skill?.Review,
                Continuation = continuation,
                Capability = contentCapability,
                ContentContinuation = contentReview,
                ProcessAction = processAction,
                NativeProcessIdentity = preparedProcess?.NativeIdentity
            };
            var binding = new ExactOperationBinding(processAction is null ? "mcp.read-only" : "mcp.process." + processAction, "mcp",
                (identity.Stdio is null ? "https-read-only" : "stdio." + (processAction ?? "read-only")) + "."
                    + stage.ToString().ToLowerInvariant() + "." + Hash(review.ToolName ?? "connection"),
                Hash(review.InputSchema ?? "connection"), reviewSource is null ? source.SourceRevision : Hash(JsonSerializer.Serialize(reviewSource)),
                source.SourceRevision,
                Hash(typeof(McpInvocation).Module.ModuleVersionId.ToString("D")),
                review.InvocationDigest,
                Hash(review.ToolName ?? "connection"), identity.Digest,
                identity.Account?.Destination.Digest ?? identity.Digest, Hash("inert-local-only-result-v1"), new(1),
                new(PermissionGrantArtifactKind.McpServer, source.Scope));
            var proposal = new HostOperationProposal(invocation, new(Guid.NewGuid()), new(1), binding,
                processAction is null ? HostOperationEffect.BoundedRead : HostOperationEffect.BoundedWrite, expiresAt);
            var presented = await presenter(proposal, generation, review,
                () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Live(token))
            { throw new InvalidOperationException("Exact native MCP endpoint/account/tool/argument approval is required."); }
            var automatic = presented.Completed is { Outcome: HostInteractionOutcome.Consumed, Grant: null,
                Admission.Basis: Kora.Core.Authorization.SessionPermission.SessionPermissionBasis.ApproveAll };
            if ((continuation is not null || contentReview is not null)
                && !automatic && (presented.Presented is null || presented.Completed.Grant?.Scope != OperationGrantScope.Once))
            {
                activity.Complete(HostOperationOutcome.Failed);
                return false;
            }
            if (processAction is not null && !automatic && presented.Completed.Grant?.Scope != OperationGrantScope.Once)
            { throw new InvalidOperationException("Native process launch and cleanup each require separate one-use consent."); }
            var authorization = new HostAuthorizationService(interactions, time);
            var consumed = await ExternalOperationGrantUse.ConsumeAsync(authorization, proposal, generation,
                presented.Presented, presented.Completed, stage == McpReviewStage.Connection, token).ConfigureAwait(false);
            if (consumed.Outcome != HostInteractionOutcome.Consumed || consumed.Admission is not { } admission
                || admission.Basis != Kora.Core.Authorization.SessionPermission.SessionPermissionBasis.ApproveAll && consumed.Grant is null)
            { throw new InvalidOperationException("The exact MCP approval is no longer current."); }
            var receipt = consumed.Grant;
            var dispatched = await authorization.DispatchAsync(invocation, admission, token).ConfigureAwait(false);
            if (dispatched.Outcome != HostInteractionOutcome.Consumed || dispatched.Admission is not { } dispatch)
            { throw new InvalidOperationException("The exact MCP policy admission was retired before dispatch."); }
            if (dispatch.Basis == Kora.Core.Authorization.SessionPermission.SessionPermissionBasis.ApproveAll)
            {
                if (updateAutomaticDisclosureAdmission is { } update) { update(dispatch); }
                else { initialAutomaticDisclosureAdmission = dispatch; }
            }
            async Task Revalidate(CancellationToken checkToken)
            {
                var current = HostActivity.RequireCurrent();
                using var validation = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Policy,
                    [new ActivityLink(current.Activity!.Context)]);
                try
                {
                    var decision = await new InteractionTransaction(interactions).RunAsync(invocation, "approval.mcp-revalidate", snapshot =>
                        (processAction is "cleanup" || Live(checkToken))
                            && snapshot.Session.Generation == generation
                            && (processAction is "cleanup" || InteractionTransaction.CanInteract(snapshot))
                            && snapshot.Policy.OtherMandatoryGatesSatisfied
                            && snapshot.EffectivePermission == dispatch.Policy && snapshot.Admissions.Contains(dispatch)
                            && (dispatch.Basis == Kora.Core.Authorization.SessionPermission.SessionPermissionBasis.ApproveAll
                                && dispatch.GrantId is null && receipt is null
                                || receipt is not null && snapshot.Grants.SingleOrDefault(item => item.Id == receipt.Id) == receipt
                                    && receipt.Status == (receipt.Scope == OperationGrantScope.Once
                                        ? OperationGrantStatus.Consumed : OperationGrantStatus.Active))
                            ? (snapshot, new(HostInteractionOutcome.Approved, "exact-mcp-use-current"))
                            : InteractionTransaction.Reject(snapshot, "exact-mcp-use-retired"), checkToken).ConfigureAwait(false);
                    if (decision.Outcome != HostInteractionOutcome.Approved
                        || processAction is not "cleanup" && !Live(checkToken))
                    { throw new InvalidOperationException("The exact MCP approval was revoked, expired or retired."); }
                    validation.Complete(HostOperationOutcome.Completed);
                }
                catch (OperationCanceledException) { validation.Complete(HostOperationOutcome.Cancelled); throw; }
                catch { validation.Complete(HostOperationOutcome.Failed); throw; }
            }
            if (processAction is "cleanup") { captureAuthority!(Revalidate); }
            else
            {
                grants.Add(Revalidate);
                captureAuthority?.Invoke(Revalidate);
            }
            activity.Complete(HostOperationOutcome.Completed);
            return true;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private async ValueTask AuthorizeAutomaticDisclosureAsync(ImmutableArray<byte> body,
        McpAccountReference account, Kora.Core.Authorization.SessionPermission.SessionPermissionAdmission admission,
        CancellationToken token)
    {
        var permissions = interactions as ISessionPermissionStore
            ?? throw new InvalidOperationException("The authoritative automatic MCP disclosure audit is unavailable.");
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(admission.Proposal.Request, HostActivityLayer.Application,
            HostOperation.Policy, [new ActivityLink(parent.Activity!.Context)]);
        var authorization = new HostAuthorizationService(interactions, time);
        try
        {
            if (!Admitted(token) || (await authorization.ValidateAdmissionAsync(admission.Proposal.Request, admission, token)
                .ConfigureAwait(false)).Outcome != HostInteractionOutcome.Approved)
            { throw new InvalidOperationException("The exact MCP disclosure policy is no longer current."); }
            var bodyDigest = Convert.ToHexStringLower(SHA256.HashData(body.AsSpan()));
            var proposal = admission.Proposal;
            var details = new AutomaticApprovalAudit(proposal.Request, admission.Policy, proposal.Binding.ActionId,
                proposal.Effect, "mcp-final-request-disclosure", proposal.Binding.TrackedContentDigest,
                proposal.ProposalId, proposal.Revision, proposal.Binding, account.Destination.Digest,
                Hash(JsonSerializer.Serialize(account)), Hash(identity.Digest + Source.SourceRevision), bodyDigest);
            await permissions.RecordAutomaticApprovalAsync(proposal.Request, admission.Policy, details,
                () => Admitted(token), token).ConfigureAwait(false);
            if (!Admitted(token) || (await authorization.ValidateAdmissionAsync(proposal.Request, admission, token)
                .ConfigureAwait(false)).Outcome != HostInteractionOutcome.Approved)
            { throw new InvalidOperationException("The MCP disclosure policy changed before sending the final bytes."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        guidanceDescriptor?.Retire();
        disposed = true;
        try { await JoinTransportAsync().ConfigureAwait(false); }
        finally { if (preparedProcess is not null) { await preparedProcess.DisposeAsync().ConfigureAwait(false); } }
    }

    internal async ValueTask JoinTransportAsync()
    {
        guidanceDescriptor?.Retire();
        if (connection is { } owned)
        {
            connection = null;
            transportJoin = DisposeTransportAsync(owned);
        }
        if (transportJoin is { } joining) { await joining.ConfigureAwait(false); }
    }

    private static async Task DisposeTransportAsync(IMcpConnection owned) =>
        await owned.DisposeAsync().ConfigureAwait(false);

    internal async Task ValidateResultSourceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Live(token)) { throw new InvalidOperationException("The original MCP result presentation was retired."); }
        await configuration.ValidateCurrentAsync(descriptor!, token).ConfigureAwait(false);
        await ValidateAccountAsync(token).ConfigureAwait(false);
        if (!Live(token)) { throw new InvalidOperationException("The original MCP result source was retired."); }
    }

    private async Task ValidateAccountAsync(CancellationToken token)
    {
        if (identity.Account is not { } account) { return; }
        var accounts = await broker.ReadAccountsAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!accounts.Any(choice => choice.Reference == account))
        { throw new InvalidOperationException("The exact MCP result's delegated account is no longer available."); }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class McpExchangeException(Exception innerException)
        : IOException("The MCP exchange failed. Review the original request before explicitly starting a new connection.", innerException);
}