using System.Text.Json;

using Kora.Application.Configuration;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>Exact host admission and execution boundary. Unqualified production profiles remain unavailable.</summary>
public sealed partial class ModelTurnHost : IAsyncDisposable, IModelTurnAdmission
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly ISessionWorkspaceStore workspace;
    private readonly ISessionWorkspaceAccess access;
    private readonly ICapabilityHostAccess host;
    private readonly ReadOnlyCapabilityRegistry tools;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<ModelTurnHost> logger;
    private readonly TimeProvider time;
    private readonly ModelProviderRegistration[] registrations;
    private readonly ProviderModeConfigurationService? providerModeConfiguration;
    private readonly ProviderProfileConfigurationService? providerProfileConfiguration;
    private readonly ISessionProviderProfileStore? sessionProfiles;
    private readonly IModelProviderRegistrationSource? registrationSource;
    private readonly HostedTransmissionReviewService? hostedReviews;
    private readonly RuntimeResourceAllocator resources;
    private static readonly TimeSpan Deadline = ModelToolProtocol.Deadline;
    private readonly Lock dispatchGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private TaskCompletionSource? quiescence;
    private volatile bool disposed;
    private Task? disposal;

    public bool IsQuiescent
    {
        get
        {
            lock (dispatchGate)
            {
                return resources.IsIdle && (quiescence is null || quiescence.Task.IsCompleted)
                    && registrations.All(item => item.Adapter?.IsQuiescent != false);
            }
        }
    }

    public ModelTurnHost(ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
        ICapabilityHostAccess host, ReadOnlyCapabilityRegistry tools, ISecurityAuditLog audit,
        ILogger<ModelTurnHost> logger, TimeProvider time, ProviderModeConfigurationService providerModeConfiguration,
        ProviderProfileConfigurationService providerProfileConfiguration, ISessionProviderProfileStore sessionProfiles)
        : this(workspace, access, host, tools, audit, logger, time,
        [
            new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null),
            new(ModelProviderSelection.CopilotCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null),
        ], providerModeConfiguration, providerProfileConfiguration, sessionProfiles)
    { }

    internal ModelTurnHost(ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
        ICapabilityHostAccess host, ReadOnlyCapabilityRegistry tools, ISecurityAuditLog audit,
        ILogger<ModelTurnHost> logger, TimeProvider time, ModelProviderRegistration[] registrations,
        ProviderModeConfigurationService? providerModeConfiguration = null, ProviderProfileConfigurationService? providerProfileConfiguration = null,
        ISessionProviderProfileStore? sessionProfiles = null, IModelProviderRegistrationSource? registrationSource = null,
        HostedTransmissionReviewService? hostedReviews = null,
        IRuntimeCapacityQualification? runtimeCapacity = null)
    {
        this.workspace = workspace;
        this.access = access;
        this.host = host;
        this.tools = tools;
        this.audit = audit;
        this.logger = logger;
        this.time = time;
        this.registrations = registrations;
        this.providerModeConfiguration = providerModeConfiguration;
        this.providerProfileConfiguration = providerProfileConfiguration;
        this.sessionProfiles = sessionProfiles;
        this.registrationSource = registrationSource;
        this.hostedReviews = hostedReviews;
        resources = new(owner, time, audit, runtimeCapacity);
    }

    public Task<ModelTurnAdmission> AdmitAsync(ModelProviderSelection selection,
        ModelContextEnvelope? context, CancellationToken cancellationToken) =>
        AdmitSelectionAsync(selection, context, HostActivity.Current, cancellationToken);

    private async Task<ModelTurnAdmission> AdmitSelectionAsync(ModelProviderSelection selection,
        ModelContextEnvelope? context, HostActivity? issuer, CancellationToken cancellationToken,
        Kora.Core.Providers.ProviderProfileTuple? profile = null, HostedTurnAuthorization? hostedAuthorization = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || issuer is null || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(issuer.Request, current.Request))
        {
            return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired));
        }
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), current.Request, selection);
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        Tag(provenance);
        var requested = StartAudit(provenance);
        ModelTurnResult result;
        ModelTurn? turn = null;
        try
        {
            var revision = access.ControlRevision;
            var reason = ContextReason(context, current.Request);
            if (cancellationToken.IsCancellationRequested)
            {
                result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, provenance);
            }
            else if (reason != ModelTurnReason.None)
            {
                result = Finish(ModelTurnOutcome.Denied, reason, provenance);
            }
            else if (!Eligible(revision))
            {
                result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed, provenance);
            }
            else
            {
                var session = await workspace.ReadMetadataAsync(current.Request.SessionId, cancellationToken).ConfigureAwait(false);
                var task = await workspace.ReadTaskAsync(current.Request.SessionId, current.Request.TaskId,
                    cancellationToken).ConfigureAwait(false);
                var registration = registrations.SingleOrDefault(item => item.Selection == selection);
                var wire = context!.Serialize();
                if (!Eligible(revision))
                {
                    result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed, provenance);
                }
                else if (session.Authority.SessionId != current.Request.SessionId || !session.Authority.IsActive
                    || session.Authority.Generation.Value <= 0 || !OwnsTask(task, current.Request, session.Authority.Generation))
                {
                    result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted, provenance);
                }
                else if (ContextReason(context, current.Request) is var expired && expired != ModelTurnReason.None)
                {
                    result = Finish(ModelTurnOutcome.Denied, expired, provenance);
                }
                else if (wire.Length > (context.Constraints?.InputUtf8Bytes ?? ModelContextEnvelope.MaximumInputUtf8Bytes))
                {
                    result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.EnvelopeLimitExceeded, provenance);
                }
                else
                {
                    if (profile is not null && registrationSource is not null
                        && (selection.Provider == ModelProviderIdentity.Ollama || hostedAuthorization is not null))
                    { registration = await registrationSource.ResolveAsync(selection, profile, cancellationToken).ConfigureAwait(false); }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Eligible(revision))
                    { result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed, provenance); }
                    else if (registration is null || registration.Selection != selection)
                    { result = Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, provenance); }
                    else if (!registration.IsQualified(time.GetUtcNow()))
                    { result = Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending, provenance); }
                    else if (selection.Provider != ModelProviderIdentity.Ollama
                        && !HostedAuthorized(registration, hostedAuthorization, context, issuer))
                    {
                        // Hosted eligibility is not destination/content approval. Local composition cannot clear this denial.
                        result = Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted, provenance);
                    }
                    else
                    {
                        turn = new(owner, issuer, provenance, context, wire, session.Authority.Generation, task!.Task.Revision,
                            revision, registration);
                        if (hostedAuthorization is not null)
                        {
                            var boundTurn = turn;
                            turn.Registration = registration with
                            {
                                Adapter = BindHosted(registration.Adapter!, hostedAuthorization, token => HostedCurrentAsync(boundTurn, token)),
                            };
                        }
                        var refreshed = profile is not null && registrationSource is not null
                            ? await RevalidateAsync(turn, cancellationToken).ConfigureAwait(false) : ModelTurnReason.None;
                        if (refreshed != ModelTurnReason.None)
                        {
                            turn = null;
                            result = Finish(ModelTurnOutcome.Denied, refreshed, provenance);
                        }
                        else { result = Finish(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, provenance);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(requested, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome, provenance));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
        CompleteAudit(requested, result);
        activity.Complete(ToActivityOutcome(result.Outcome));
        return new(result, turn);
    }

    public async Task<ModelTurnResult> RunAsync(ModelTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var current = HostActivity.Current;
        if (turn.Owner != owner || current is null || current.Activity!.IsStopped
            || current.Outcome != HostOperationOutcome.Unknown || !ReferenceEquals(current.Request, turn.Provenance.Request)
            || turn.Issuer.Activity!.IsStopped || turn.Issuer.Outcome != HostOperationOutcome.Unknown)
        {
            return Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired);
        }
        if (!turn.TryUse())
        {
            return Finish(ModelTurnOutcome.Denied, ModelTurnReason.TurnAlreadyUsed, turn.Provenance);
        }
        if (disposed) { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed, turn.Provenance); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Runtime);
        Tag(turn.Provenance);
        var requested = StartAudit(turn.Provenance);
        using var deadline = new CancellationTokenSource(turn.Context.Constraints is { } constraints
            ? TimeSpan.FromMilliseconds(constraints.DeadlineMilliseconds)
            : turn.Registration.Adapter is CopilotModelTurnAdapter or ClaudeModelTurnAdapter ? HostedTurnAuthorization.MaximumDuration : Deadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, lifetime.Token,
            turn.ResourceLease?.Evidence?.Revoked ?? CancellationToken.None);
        ModelTurnResult result;
        try
        {
            result = await ExecuteAsync(turn, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            result = Finish(cancellationToken.IsCancellationRequested ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.TimedOut,
                cancellationToken.IsCancellationRequested ? ModelTurnReason.CallerCancelled : ModelTurnReason.DeadlineExceeded,
                turn.Provenance);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(requested, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome, turn.Provenance));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
        finally
        {
            turn.IsRunning = false;
        }
        CompleteAudit(requested, result);
        activity.Complete(ToActivityOutcome(result.Outcome));
        turn.TerminalResult = result;
        return result;
    }

    private async Task<ModelTurnResult> ExecuteAsync(ModelTurn turn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await CustomizationDiscoveryCurrent(turn, token).ConfigureAwait(false))
        { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.ToolResultRejected, turn.Provenance); }
        if (!await SessionDiscoveryCurrentAsync(turn, token).ConfigureAwait(false))
        { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted, turn.Provenance); }
        var reason = await RevalidateAsync(turn, token).ConfigureAwait(false);
        if (reason != ModelTurnReason.None) { return Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
        if (turn.Profile is not null)
        {
            var profileReason = await RevalidateProfileAsync(turn, token).ConfigureAwait(false);
            if (profileReason != ModelTurnReason.None)
            { return Finish(ModelTurnOutcome.Unavailable, profileReason, turn.Provenance); }
            reason = await RevalidateAsync(turn, token).ConfigureAwait(false);
            if (reason != ModelTurnReason.None) { return Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
        }
        var released = AcquireDispatch(turn);
        if (released is null)
        {
            return Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.ProviderBusy, turn.Provenance);
        }
        // Cancellation closes presentation/tool authority promptly, but the slot stays occupied until actual adapter termination.
        turn.IsRunning = true;
        var transferred = false;
        ModelProviderReply reply;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!ConsumerAdmissionCurrent(turn))
            { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed, turn.Provenance); }
            var pending = RunAdapterAsync(turn, token);
            try { reply = await pending.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                transferred = true;
                _ = ObserveLateAsync(pending, released,
                    HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Runtime), turn.Provenance);
                throw;
            }
        }
        finally
        {
            turn.IsRunning = false;
            if (!transferred) { ReleaseDispatch(released); }
        }
        token.ThrowIfCancellationRequested();
        if (turn.HasForbiddenToolAttempt)
        { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted, turn.Provenance); }
        reason = await RevalidateContinuationAsync(turn, token).ConfigureAwait(false);
        if (reason != ModelTurnReason.None) { return Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
        if (reply is null || reply.ReportedSelection != turn.Provenance.Selection || !Enum.IsDefined(reply.Outcome)
            || !Enum.IsDefined(reply.Reason)
            || reply.Outcome == ModelTurnOutcome.Succeeded && (!ValidResponse(reply.Response)
                || JsonSerializer.SerializeToUtf8Bytes(reply.Response).Length >
                    (turn.Context.Constraints?.OutputUtf8Bytes ?? ModelContextEnvelope.MaximumOutputUtf8Bytes)))
        {
            return Finish(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, turn.Provenance);
        }
        return Finish(reply.Outcome, reply.Reason, turn.Provenance,
            reply.Outcome == ModelTurnOutcome.Succeeded ? reply.Response : null);
    }

    internal async ValueTask<CapabilityReply> InvokeToolAsync(ModelTurn turn, string id, string input,
        CancellationToken turnToken, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, token);
        linked.Token.ThrowIfCancellationRequested();
        // Refuse before asynchronous source validation so an unawaited callback cannot race a successful answer.
        if (turn.Owner == owner && (turn.Context.ToolMode == ModelToolMode.None
            || (turn.Context.McpTool is { } external
                ? !string.Equals(external.Id, id, StringComparison.Ordinal)
                : !turn.Context.Tools.Any(tool => string.Equals(tool.Id, id, StringComparison.Ordinal)))))
        {
            turn.RejectToolAttempt();
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "model-tools-not-permitted");
        }
        var current = HostActivity.Current;
        if (turn.Owner != owner || !turn.IsRunning || current is null || current.Activity!.IsStopped
            || !ReferenceEquals(current.Request, turn.Provenance.Request)
            || await RevalidateContinuationAsync(turn, linked.Token).ConfigureAwait(false) != ModelTurnReason.None)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "model-turn-not-admitted");
        }
        if (!turn.TryAdmitTool())
        {
            turn.RejectToolAttempt();
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "tool-call-budget-exhausted");
        }
        turn.PublishExecutionStep?.Invoke(Kora.Core.Conversation.ConversationExecutionStep.ReadingObservation);
        var reply = customizationDiscoveries.TryGetValue(turn, out var customizations)
            ? await customizations.Invoke(id, input, linked.Token).ConfigureAwait(false)
            : sessionDiscoveries.TryGetValue(turn, out var discovery)
            ? await discovery.InvokeAsync(id, input, linked.Token).ConfigureAwait(false)
            : turn.Context.ToolMode == ModelToolMode.McpReadOnly
            ? mcpInvocations.TryGetValue(turn, out var mcp)
                ? await mcp.InvokeAsync(turn.Provenance, id, input, linked.Token).ConfigureAwait(false)
                : new(CapabilityOutcome.Denied, "mcp-reviewed-workflow-unavailable")
            : turn.Context.ToolMode == ModelToolMode.LocalWebPage
            ? webPages.TryGetValue(turn, out var webPage)
                ? await webPage.InvokeAsync(turn.Provenance, id, input, linked.Token).ConfigureAwait(false)
                : new(CapabilityOutcome.Denied, "web-page-approval-workflow-unavailable")
            : tools.Invoke(tools.Admit(CapabilityLane.Execution), id, input, linked.Token);
        if (await RevalidateContinuationAsync(turn, linked.Token).ConfigureAwait(false) != ModelTurnReason.None)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "model-turn-admission-changed");
        }
        turn.PublishExecutionStep?.Invoke(Kora.Core.Conversation.ConversationExecutionStep.GeneratingAnswer);
        return reply;
    }

    private async ValueTask<ModelTurnReason> RevalidateProfileAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.Profile is not { } profile) { return ModelTurnReason.None; }
        if (sessionProfiles is null || providerProfileConfiguration is null)
        { return ModelTurnReason.SessionNotAdmitted; }
        var observed = await sessionProfiles.ReadProviderProfile(turn.Provenance.Request.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (observed.Held || observed.Session.SessionId != turn.Provenance.Request.SessionId
            || !observed.Session.IsActive || observed.Session.Generation != turn.SessionGeneration)
        { return ModelTurnReason.SessionNotAdmitted; }
        // Later preference edits affect later turns; only the captured tuple's live authority is revalidated.
        return await providerProfileConfiguration.ValidateTurnAsync(profile, token).ConfigureAwait(false)
            ? ModelTurnReason.None : ModelTurnReason.UnregisteredModel;
    }

    private async ValueTask<ModelTurnReason> RevalidateContinuationAsync(ModelTurn turn, CancellationToken token)
    {
        if (!await CustomizationDiscoveryCurrent(turn, token).ConfigureAwait(false))
        { return ModelTurnReason.ToolResultRejected; }
        if (!await SessionDiscoveryCurrentAsync(turn, token).ConfigureAwait(false))
        { return ModelTurnReason.SessionNotAdmitted; }
        var reason = await RevalidateAsync(turn, token).ConfigureAwait(false);
        if (reason != ModelTurnReason.None) { return reason; }
        reason = await RevalidateProfileAsync(turn, token).ConfigureAwait(false);
        return reason != ModelTurnReason.None ? reason : await RevalidateAsync(turn, token).ConfigureAwait(false);
    }

    private async ValueTask<ModelTurnReason> RevalidateAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.ResourceLease is { } resource && !resources.IsCurrent(resource))
        { return ModelTurnReason.QualificationPending; }
        if (!ConsumerAdmissionCurrent(turn)) { return ModelTurnReason.HostAdmissionClosed; }
        await ValidateConsumerSourceAsync(turn, token).ConfigureAwait(false);
        if (turn.Policy is { } policy && !IsCurrentPolicy(policy)) { return ModelTurnReason.PolicyChanged; }
        var reason = ContextReason(turn.Context, turn.Provenance.Request);
        if (reason != ModelTurnReason.None) { return reason; }
        if (!Eligible(turn.ControlRevision) || !ConsumerAdmissionCurrent(turn)) { return ModelTurnReason.HostAdmissionClosed; }
        var session = await workspace.ReadMetadataAsync(turn.Provenance.Request.SessionId, token).ConfigureAwait(false);
        var task = await workspace.ReadTaskAsync(turn.Provenance.Request.SessionId, turn.Provenance.Request.TaskId,
            token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Eligible(turn.ControlRevision) || !ConsumerAdmissionCurrent(turn)) { return ModelTurnReason.HostAdmissionClosed; }
        if (session.Authority.SessionId != turn.Provenance.Request.SessionId || !session.Authority.IsActive
            || turn.ConversationTarget is { } target && target != session
            || session.Authority.Generation != turn.SessionGeneration
            || !OwnsTask(task, turn.Provenance.Request, turn.SessionGeneration) || task!.Task.Revision != turn.TaskRevision)
        {
            return ModelTurnReason.SessionNotAdmitted;
        }
        reason = ContextReason(turn.Context, turn.Provenance.Request);
        if (reason != ModelTurnReason.None) { return reason; }
        if (turn.Issuer.Activity!.IsStopped || turn.Issuer.Outcome != HostOperationOutcome.Unknown)
        {
            return ModelTurnReason.HostContextRequired;
        }
        return turn.Registration.IsQualified(time.GetUtcNow()) ? ModelTurnReason.None : ModelTurnReason.QualificationPending;
    }

    private bool Eligible(long revision) =>
        !disposed && host.IsCurrentHost && access.CanControl && access.ControlRevision == revision;

    private static bool OwnsTask(HostTaskObservation? task, HostRequest request, HostRevision generation) =>
        task is not null && task.CurrentSource && task.Generation == generation
        && task.Task.Request == request && !task.Task.IsTerminal;

    private ModelTurnReason ContextReason(ModelContextEnvelope? context, HostRequest request) =>
        context is null ? ModelTurnReason.ContextMissing
        : context.Request != request ? ModelTurnReason.ContextMismatch
        : time.GetUtcNow() < context.CreatedAt || time.GetUtcNow() >= context.ExpiresAt
            ? ModelTurnReason.ContextExpired : ModelTurnReason.None;

    private static bool ValidResponse(LocalModelResponse? response) =>
        response is { Answer: not null, Action: null, GrantChange: null, Question: null }
        && !string.IsNullOrWhiteSpace(response.Answer)
        && JsonSerializer.SerializeToUtf8Bytes(response).Length <= ModelContextEnvelope.MaximumOutputUtf8Bytes;

    private SecurityAuditEvent StartAudit(ModelTurnProvenance provenance)
    {
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "model.turn", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System,
            provenance.Turn.Value.ToString("D"));
        audit.Write(requested);
        return requested;
    }

    private void CompleteAudit(SecurityAuditEvent requested, ModelTurnResult result) =>
        audit.Write(requested.WithOutcome(result.Outcome switch
        {
            ModelTurnOutcome.Succeeded => SecurityAuditOutcome.Succeeded,
            ModelTurnOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
            ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => SecurityAuditOutcome.Denied,
            ModelTurnOutcome.Unknown => SecurityAuditOutcome.Unknown,
            _ => SecurityAuditOutcome.Failed,
        }));

    private ModelTurnResult Finish(ModelTurnOutcome outcome, ModelTurnReason reason,
        ModelTurnProvenance? provenance = null, LocalModelResponse? response = null)
    {
        Result(logger, outcome, reason, provenance?.Turn.Value, provenance?.Selection.Provider,
            provenance?.Selection.Model.Value, provenance?.Selection.Revision.Value);
        return new(outcome, reason, provenance, response);
    }

    private static HostOperationOutcome ToActivityOutcome(ModelTurnOutcome outcome) => outcome switch
    {
        ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
        ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
        ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
        _ => HostOperationOutcome.Failed,
    };

    private static void Tag(ModelTurnProvenance provenance)
    {
        var activity = HostActivity.RequireCurrent().Activity!;
        activity.SetTag("kora.model.turn.id", provenance.Turn.Value);
        activity.SetTag("kora.provider", provenance.Selection.Provider.ToString());
        activity.SetTag("kora.model.id", provenance.Selection.Model.Value);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Observes this host's already-dispatched adapter off the UI context; no synchronous join or UI dependency.")]
    private async Task ObserveLateAsync(Task<ModelProviderReply> pending, DispatchLease released,
        Func<HostActivity> continuation, ModelTurnProvenance provenance)
    {
        using var activity = continuation();
        var outcome = HostOperationOutcome.Cancelled;
        try
        {
            await pending.ConfigureAwait(false);
            LateResponse(logger, provenance.Turn.Value);
        }
        catch (Exception exception)
        {
            if (released.Lane is not null)
            { lock (dispatchGate) { lateFailures.Add(exception); } }
            Fault(logger, exception.GetType().Name);
            outcome = HostOperationOutcome.Unknown;
        }
        finally { ReleaseDispatch(released); }
        activity.Complete(outcome);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (dispatchGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        List<Exception> failures = [];
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        Task outstanding;
        lock (dispatchGate) { outstanding = quiescence?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        await resources.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
        lock (dispatchGate) { failures.AddRange(lateFailures); }
        if (!IsQuiescent)
        { failures.Add(new InvalidOperationException("The model runtime has not confirmed resource quiescence.")); }
        lock (policyGate) { policies.Clear(); }
        try
        {
            foreach (var adapter in registrations.Select(item => item.Adapter).OfType<IAsyncDisposable>()
                .Distinct<IAsyncDisposable>(ReferenceEqualityComparer.Instance))
            {
                try { await adapter.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { failures.Add(exception); }
            }
        }
        finally { lifetime.Dispose(); }
        if (failures.Count == 1) { throw failures[0]; }
        if (failures.Count != 0) { throw new AggregateException("Model resource closure failed.", failures); }
    }
}