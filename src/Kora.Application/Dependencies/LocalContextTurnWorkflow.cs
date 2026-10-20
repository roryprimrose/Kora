using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>The shared durable admission and terminal workflow for reviewed, volatile local source turns.</summary>
public sealed partial class LocalContextTurnWorkflow(
    ISessionWorkspaceStore workspace,
    ILocalContextTaskStore contextTasks,
    SessionWorkspaceService sessions,
    HostTaskCoordinator coordinator,
    ISessionWorkspaceAccess access,
    IModelTurnAdmission admission,
    IModelTurnExecution execution,
    ISecurityAuditLog audit,
    ILogger<LocalContextTurnWorkflow> logger) : HostControlAdmission(workspace, coordinator), IModelContextRunner
{
    protected override async ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        (await sessions.CreateLocalContextSessionAsync(request, eligible, token).ConfigureAwait(false)).Authority;

    public async Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
        CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceCurrent);
        var request = target.Request;
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(current.Request, target.SourceRequest) || current.Request.Origin != request.Origin)
        {
            return new(new LocalContextTurnReceipt(request, ModelTurnOutcome.Denied, LocalContextTurnReason.OriginalInputRequired));
        }
        if (!target.TryConsume())
        {
            return new(new LocalContextTurnReceipt(request, ModelTurnOutcome.Denied, LocalContextTurnReason.TargetAlreadyUsed));
        }
        var prepared = new AdmittedContextTurn(request, target.SourceRequest);
        var completion = RunLifecycleAsync(target, prepared, current, sourceCurrent, validateSource, token);
        prepared.BindCompletion(completion);
        var result = await prepared.ReadAdmissionAsync().ConfigureAwait(false);
        if (result.Turn is null) { await prepared.DisposeAsync().ConfigureAwait(false); }
        return result;
    }

    private async Task<LocalContextTurnReceipt> RunLifecycleAsync(LocalContextSessionTarget target,
        AdmittedContextTurn prepared, HostActivity current, Func<bool> eligible,
        Func<CancellationToken, Task>? validateSource, CancellationToken token)
    {
        var request = target.Request;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, prepared.Revocation);
        token = linked.Token;
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "model.context.local", SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            request.TaskId.Value.ToString("D"));
        using var auditActivity = HostActivity.BeginAudit(request, requested, [new(current.Activity!.Context)]);
        LocalContextTurnReceipt receipt;
        var requestedCommitted = false;
        var revision = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == revision && eligible()
            && !current.Activity!.IsStopped && current.Outcome == HostOperationOutcome.Unknown;
        try
        {
            audit.Write(requested);
            requestedCommitted = true;
            receipt = await RunExactAsync(request, Live,
                (admitted, cancellation) => ExecuteAsync(target, prepared, admitted, validateSource, cancellation),
                value => ActivityOutcome(value.Outcome), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            receipt = new(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            HoldEvidenceUnavailable();
            receipt = new(request, ModelTurnOutcome.Unknown, LocalContextTurnReason.EvidenceUnavailable);
        }
        try
        {
            if (requestedCommitted) { audit.Write(requested.WithOutcome(AuditOutcome(receipt.Outcome))); }
            if (receipt.Outcome == ModelTurnOutcome.Succeeded)
            {
                try
                {
                    await RequireSourceAsync(Live, validateSource, token).ConfigureAwait(false);
                    var actual = await Workspace.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false);
                    if (!actual.Authority.IsActive || actual.Authority.Generation != prepared.SessionGeneration)
                    { throw new SourceNotCurrentException(); }
                    await RequireSourceAsync(Live, validateSource, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    receipt = new(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled, receipt.DurableState);
                }
                catch (SourceNotCurrentException)
                {
                    receipt = SourceClosed(request, receipt.DurableState);
                }
                if (receipt.Outcome != ModelTurnOutcome.Succeeded)
                {
                    audit.Write(requested.WithOutcome(AuditOutcome(receipt.Outcome)));
                }
            }
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            HoldEvidenceUnavailable();
            receipt = new(request, ModelTurnOutcome.Unknown, LocalContextTurnReason.EvidenceUnavailable, receipt.DurableState);
        }
        Result(logger, receipt.Outcome, receipt.Reason, receipt.DurableState);
        auditActivity.Complete(ActivityOutcome(receipt.Outcome));
        prepared.RejectIfNotAdmitted(receipt);
        prepared.Retire();
        return receipt;
    }

    private async Task<LocalContextTurnReceipt> ExecuteAsync(LocalContextSessionTarget target,
        AdmittedContextTurn prepared, Func<bool> eligible, Func<CancellationToken, Task>? validateSource,
        CancellationToken token)
    {
        var request = target.Request;
        HostTaskRecord? prior = null;
        var executionEntered = false;
        var admissionToken = token;
        CancellationToken submissionToken = default;
        Func<CancellationToken, Task>? finalValidation = null;
        LocalContextTurnReceipt receipt;
        try
        {
            await RequireSourceAsync(eligible, validateSource, token).ConfigureAwait(false);
            var session = target.CreatesSession
                ? await CreateSessionAsync(request, eligible, token).ConfigureAwait(false)
                : (await Workspace.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false)).Authority;
            await RequireSourceAsync(eligible, validateSource, token).ConfigureAwait(false);
            if (session.SessionId != request.SessionId || !session.IsActive
                || session.Generation.Value <= 0
                || target.ExpectedGeneration is { } expected && session.Generation != expected)
            {
                return new(request, ModelTurnOutcome.Denied, LocalContextTurnReason.SessionNotCurrent);
            }
            prior = await Workspace.RecordControlIntentAsync(request, token).ConfigureAwait(false);
            async Task ValidateTurnAsync(CancellationToken cancellation)
            {
                admissionToken.ThrowIfCancellationRequested();
                submissionToken.ThrowIfCancellationRequested();
                if (!OwnsLiveContext(request)) { throw new SourceNotCurrentException(); }
                await RequireSourceAsync(eligible, validateSource, cancellation).ConfigureAwait(false);
                admissionToken.ThrowIfCancellationRequested();
                submissionToken.ThrowIfCancellationRequested();
                var actual = await Workspace.ReadMetadataAsync(request.SessionId, cancellation).ConfigureAwait(false);
                if (actual.Authority != session) { throw new SourceNotCurrentException(); }
                await RequireSourceAsync(eligible, validateSource, cancellation).ConfigureAwait(false);
            }
            finalValidation = ValidateTurnAsync;
            await ValidateTurnAsync(token).ConfigureAwait(false);
            prior = await contextTasks.AdmitLocalContextDispatchAsync(request, session.Generation, eligible, token).ConfigureAwait(false);
            var observed = await Workspace.ReadTaskAsync(request.SessionId, request.TaskId, token).ConfigureAwait(false);
            if (observed?.Task != prior || !observed.CurrentSource || observed.Generation != session.Generation
                || prior.State != HostTaskState.DispatchRecorded || prior.Request != request)
            {
                throw new InvalidDataException("Local context admission has no exact current-source durable dispatch.");
            }
            await ValidateTurnAsync(token).ConfigureAwait(false);
            var submitted = await prepared.ReceiveAsync(session.Generation, token).ConfigureAwait(false);
            submissionToken = submitted.Token;
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, submitted.Token);
            token = runCancellation.Token;
            if (submitted.Rejection is { } rejection) { throw new ContextRejectedException(rejection); }
            // RunAsync accepts a non-null envelope; rejected submissions were handled above.
            var context = submitted.Context!;
            if (!ReferenceEquals(context.Request, request) || context.ToolMode != ModelToolMode.None || context.Evidence.IsEmpty
                || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.LocalOnly))
            {
                throw new ContextRejectedException(LocalContextTurnReason.ContextMismatch);
            }
            context.RequireCompleteInputLimit();
            await ValidateTurnAsync(token).ConfigureAwait(false);
            var model = await admission.AdmitAsync(ModelTurnChoice.Local, context, null, token).ConfigureAwait(false);
            var result = model.Result;
            if (model.Turn is { } turn)
            {
                await ValidateTurnAsync(token).ConfigureAwait(false);
                executionEntered = true;
                result = await execution.RunAsync(turn, eligible, token, ValidateTurnAsync).ConfigureAwait(false);
                if (executionEntered) { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            }
            token.ThrowIfCancellationRequested();
            await ValidateTurnAsync(token).ConfigureAwait(false);
            if (!Enum.IsDefined(result.Outcome) || !Enum.IsDefined(result.Reason)
                || result.Outcome == ModelTurnOutcome.Succeeded
                    && (model.Turn is null || !ReferenceEquals(model.Turn.TerminalResult, result)
                        || result.Provenance != model.Turn.Provenance
                        || result.Response is not { Answer: not null, Action: null, GrantChange: null, Question: null }))
            {
                result = new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse);
            }
            receipt = new(request, result.Outcome, Reason(result.Outcome), ModelResult: result);
        }
        catch (OperationCanceledException)
        {
            if (executionEntered) { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            receipt = new(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled);
        }
        catch (SourceNotCurrentException)
        {
            if (executionEntered) { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            receipt = SourceClosed(request);
        }
        catch (ContextRejectedException exception)
        {
            receipt = new(request, ModelTurnOutcome.Denied, exception.Reason);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
            HoldEvidenceUnavailable();
            receipt = new(request, ModelTurnOutcome.Unknown, LocalContextTurnReason.EvidenceUnavailable);
        }
        if (prior is null) { return receipt; }
        var state = TaskState(receipt.Outcome, prior.State == HostTaskState.DispatchRecorded);
        try
        {
            var terminal = await Coordinator.RecordOutcomeAsync(prior, state, CancellationToken.None).ConfigureAwait(false);
            var observed = await Workspace.ReadTaskAsync(request.SessionId, request.TaskId, CancellationToken.None).ConfigureAwait(false);
            if (observed?.Task != terminal)
            {
                throw new InvalidDataException("The exact local context terminal receipt could not be confirmed. Do not retry.");
            }
            if (receipt.Outcome == ModelTurnOutcome.Succeeded)
            {
                try
                {
                    // Success is assigned only after ValidateTurnAsync was installed and executed.
                    await finalValidation!(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled, terminal.State);
                }
                catch (SourceNotCurrentException)
                {
                    return SourceClosed(request, terminal.State);
                }
            }
            return receipt with { DurableState = terminal.State };
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            HoldEvidenceUnavailable();
            return new(request, ModelTurnOutcome.Unknown, LocalContextTurnReason.EvidenceUnavailable);
        }
    }

    private static async Task RequireSourceAsync(Func<bool> eligible,
        Func<CancellationToken, Task>? validateSource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new SourceNotCurrentException(); }
        if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new SourceNotCurrentException(); }
    }

    private sealed class SourceNotCurrentException : InvalidOperationException;

    private LocalContextTurnReceipt SourceClosed(HostRequest request, HostTaskState? state = null) => IsDisposed
        ? new(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled, state)
        : new(request, ModelTurnOutcome.Denied, LocalContextTurnReason.SourceNotCurrent, state);

    private static LocalContextTurnReason Reason(ModelTurnOutcome outcome) => outcome switch
    {
        ModelTurnOutcome.Succeeded => LocalContextTurnReason.Completed,
        ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalContextTurnReason.ModelDenied,
        ModelTurnOutcome.Cancelled => LocalContextTurnReason.Cancelled,
        ModelTurnOutcome.Unknown => LocalContextTurnReason.ModelOutcomeUncertain,
        _ => LocalContextTurnReason.ModelUnavailable,
    };

    private static HostTaskState TaskState(ModelTurnOutcome outcome, bool dispatched) => outcome switch
    {
        ModelTurnOutcome.Succeeded => HostTaskState.Succeeded,
        ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => HostTaskState.Denied,
        ModelTurnOutcome.Cancelled => dispatched ? HostTaskState.Unknown : HostTaskState.Cancelled,
        ModelTurnOutcome.Unknown or ModelTurnOutcome.TimedOut => HostTaskState.Unknown,
        _ => HostTaskState.Unavailable,
    };

    private static SecurityAuditOutcome AuditOutcome(ModelTurnOutcome outcome) => outcome switch
    {
        ModelTurnOutcome.Succeeded => SecurityAuditOutcome.Succeeded,
        ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => SecurityAuditOutcome.Denied,
        ModelTurnOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
        ModelTurnOutcome.Unknown => SecurityAuditOutcome.Unknown,
        _ => SecurityAuditOutcome.Failed,
    };

    private static HostOperationOutcome ActivityOutcome(ModelTurnOutcome outcome) => outcome switch
    {
        ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
        ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
        ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
        _ => HostOperationOutcome.Failed,
    };
}