using System.Text.Json;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>R04-R08 host boundary. Production profiles remain unavailable; no adapter is composed.</summary>
public sealed partial class ModelTurnHost : IAsyncDisposable
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
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private readonly Lock dispatchGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private TaskCompletionSource? quiescence;
    private volatile bool disposed;
    private Task? disposal;

    public bool IsQuiescent { get { lock (dispatchGate) { return quiescence is null || quiescence.Task.IsCompleted; } } }

    public ModelTurnHost(ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
        ICapabilityHostAccess host, ReadOnlyCapabilityRegistry tools, ISecurityAuditLog audit,
        ILogger<ModelTurnHost> logger, TimeProvider time)
        : this(workspace, access, host, tools, audit, logger, time,
        [
            new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null),
            new(ModelProviderSelection.CopilotCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null),
        ]) { }

    internal ModelTurnHost(ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access,
        ICapabilityHostAccess host, ReadOnlyCapabilityRegistry tools, ISecurityAuditLog audit,
        ILogger<ModelTurnHost> logger, TimeProvider time, ModelProviderRegistration[] registrations)
    {
        this.workspace = workspace;
        this.access = access;
        this.host = host;
        this.tools = tools;
        this.audit = audit;
        this.logger = logger;
        this.time = time;
        this.registrations = registrations;
    }

    public async Task<ModelTurnAdmission> AdmitAsync(ModelProviderSelection selection,
        ModelContextEnvelope? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
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
                else if (wire.Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
                {
                    result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.EnvelopeLimitExceeded, provenance);
                }
                else if (registration is null)
                {
                    result = Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, provenance);
                }
                else if (!registration.IsQualified(time.GetUtcNow()))
                {
                    result = Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending, provenance);
                }
                else if (selection.Provider != ModelProviderIdentity.Ollama)
                {
                    // Hosted eligibility is not an exact destination/content approval. No egress authority exists in this slice.
                    result = Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted, provenance);
                }
                else
                {
                    turn = new(owner, current, provenance, context, wire, session.Authority.Generation, task!.Task.Revision,
                        revision, registration);
                    result = Finish(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance);
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
        using var deadline = new CancellationTokenSource(Deadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, lifetime.Token);
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
        return result;
    }

    private async Task<ModelTurnResult> ExecuteAsync(ModelTurn turn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reason = await RevalidateAsync(turn, token).ConfigureAwait(false);
        if (reason != ModelTurnReason.None) { return Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
        TaskCompletionSource released;
        lock (dispatchGate)
        {
            if (!IsQuiescent)
            {
                return Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.ProviderBusy, turn.Provenance);
            }
            quiescence = released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        // Cancellation closes presentation/tool authority promptly, but the slot stays occupied until actual adapter termination.
        turn.IsRunning = true;
        var transferred = false;
        ModelProviderReply reply;
        try
        {
            token.ThrowIfCancellationRequested();
            var pending = turn.Registration.Adapter!.RunAsync(turn.Provenance, turn.Wire.AsMemory(),
                (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken), token);
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
            if (!transferred) { released.TrySetResult(); }
        }
        token.ThrowIfCancellationRequested();
        reason = await RevalidateAsync(turn, token).ConfigureAwait(false);
        if (reason != ModelTurnReason.None) { return Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
        if (reply is null || reply.ReportedSelection != turn.Provenance.Selection || !Enum.IsDefined(reply.Outcome)
            || reply.Outcome == ModelTurnOutcome.Succeeded && !ValidResponse(reply.Response))
        {
            return Finish(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, turn.Provenance);
        }
        return Finish(reply.Outcome, ModelTurnReason.ProviderOutcome, turn.Provenance,
            reply.Outcome == ModelTurnOutcome.Succeeded ? reply.Response : null);
    }

    internal async ValueTask<CapabilityReply> InvokeToolAsync(ModelTurn turn, string id, string input,
        CancellationToken turnToken, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, token);
        linked.Token.ThrowIfCancellationRequested();
        var current = HostActivity.Current;
        if (turn.Owner != owner || !turn.IsRunning || current is null || current.Activity!.IsStopped
            || !ReferenceEquals(current.Request, turn.Provenance.Request)
            || await RevalidateAsync(turn, linked.Token).ConfigureAwait(false) != ModelTurnReason.None)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "model-turn-not-admitted");
        }
        var reply = tools.Invoke(tools.Admit(CapabilityLane.Execution), id, input, linked.Token);
        if (await RevalidateAsync(turn, linked.Token).ConfigureAwait(false) != ModelTurnReason.None)
        {
            ToolDenied(logger);
            return new(CapabilityOutcome.Denied, "model-turn-admission-changed");
        }
        return reply;
    }

    private async ValueTask<ModelTurnReason> RevalidateAsync(ModelTurn turn, CancellationToken token)
    {
        var reason = ContextReason(turn.Context, turn.Provenance.Request);
        if (reason != ModelTurnReason.None) { return reason; }
        if (!Eligible(turn.ControlRevision)) { return ModelTurnReason.HostAdmissionClosed; }
        var session = await workspace.ReadMetadataAsync(turn.Provenance.Request.SessionId, token).ConfigureAwait(false);
        var task = await workspace.ReadTaskAsync(turn.Provenance.Request.SessionId, turn.Provenance.Request.TaskId,
            token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Eligible(turn.ControlRevision)) { return ModelTurnReason.HostAdmissionClosed; }
        if (session.Authority.SessionId != turn.Provenance.Request.SessionId || !session.Authority.IsActive
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
    private async Task ObserveLateAsync(Task<ModelProviderReply> pending, TaskCompletionSource released,
        Func<HostActivity> continuation, ModelTurnProvenance provenance)
    {
        try
        {
            await pending.ConfigureAwait(false);
            using var activity = continuation();
            LateResponse(logger, provenance.Turn.Value);
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            using var activity = continuation();
            Fault(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Unknown);
        }
        finally { released.TrySetResult(); }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (dispatchGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        Task outstanding;
        lock (dispatchGate) { outstanding = quiescence?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        lifetime.Dispose();
    }
}
