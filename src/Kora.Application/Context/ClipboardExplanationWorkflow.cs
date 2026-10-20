using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Context;

public sealed partial class ClipboardExplanationWorkflow(
    ClipboardSnapshotBroker snapshots, ClipboardRead read, ClipboardExplanationAdmission admission,
    ModelTurnHost models, ISecurityAuditLog audit, TimeProvider time, ILogger<ClipboardExplanationWorkflow> logger,
    IClipboardSourceUsePolicy sourceUsePolicy)
    : IClipboardExplanationWorkflow, IDisposable
{
    private readonly Lock gate = new();
    private CancellationTokenSource? pending;
    private TaskCompletionSource? completion;
    private ClipboardSnapshot? answeredSource;
    private Func<bool>? answerEligible;
    private string? answer;
    private DateTimeOffset answerExpiresAt;
    private bool disposed;
    private long generation;

    public event EventHandler? Changed;
    public bool IsQuiescent { get { lock (gate) { return pending is null && models.IsQuiescent; } } }
    public string? CurrentExplanation
    {
        get
        {
            lock (gate)
            {
                if (!disposed && answeredSource is { } source && answerEligible!()
                    && ReferenceEquals(snapshots.Current, source) && IsFresh(source)
                    && time.GetUtcNow() < answerExpiresAt) { return answer; }
                RetireAnswer();
                return null;
            }
        }
    }

    private bool IsFresh(ClipboardSnapshot source) =>
        time.GetUtcNow() >= source.CapturedAt && time.GetUtcNow() < source.CapturedAt + ClipboardExplanationContext.Lifetime;

    public async Task<ClipboardExplanationResult> ExplainAsync(Guid? reviewedSnapshotId, Func<bool> eligible, CancellationToken token)
    {
        var input = HostActivity.RequireCurrent();
        var origin = input.Request.Origin;
        var inputContext = input.Activity!.Context;
        if (disposed || !ClipboardCommand.IsDeliberateOrigin(origin) || !eligible())
        { return Report(new(ClipboardExplanationOutcome.Denied)); }
        if (reviewedSnapshotId is null)
        {
            var captured = snapshots.Current is null
                ? await read.ExecuteAsync(eligible, token).ConfigureAwait(false) : ClipboardOutcome.Reused;
            return Report(new(captured is ClipboardOutcome.Captured or ClipboardOutcome.Reused
                ? ClipboardExplanationOutcome.ReviewRequired : ClipboardExplanationOutcome.Unavailable, captured));
        }
        var source = snapshots.Current;
        if (source is null || source.SnapshotId != reviewedSnapshotId.Value)
        { return Report(new(ClipboardExplanationOutcome.Stale)); }
        if (!IsFresh(source)) { return Report(new(ClipboardExplanationOutcome.Expired)); }
        CancellationTokenSource cancellation;
        TaskCompletionSource admittedCompletion;
        long admittedGeneration;
        lock (gate)
        {
            if (pending is not null || !models.IsQuiescent) { return Report(new(ClipboardExplanationOutcome.Busy)); }
            pending = cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            completion = admittedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            admittedGeneration = ++generation;
            RetireAnswer();
        }
        bool Current()
        {
            lock (gate)
            {
                return !disposed && generation == admittedGeneration && !cancellation.IsCancellationRequested
                    && eligible() && ReferenceEquals(snapshots.Current, source) && IsFresh(source);
            }
        }
        void SourceChanged(object? sender, EventArgs args)
        {
            lock (gate)
            {
                if (ReferenceEquals(pending, cancellation) && !Current()) { cancellation.Cancel(); }
            }
        }
        snapshots.Changed += SourceChanged;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
            if (!Current()) { return Report(new(ClipboardExplanationOutcome.Cancelled)); }
            var sourceUse = await EvaluateSourceUseAsync(source, Current, cancellation.Token).ConfigureAwait(false);
            if (sourceUse is not null) { return Report(sourceUse); }
            string? completedAnswer = null;
            DateTimeOffset completedExpiry = default;
            var completed = await admission.RunAsync(origin, Current, async (request, current, admitted) =>
            {
                using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Policy,
                    [new ActivityLink(inputContext)]);
                activity.Activity!.SetTag("kora.clipboard.source.id", source.SourceId);
                activity.Activity.SetTag("kora.evidence.id", source.SnapshotId);
                activity.Activity.SetTag("kora.clipboard.capture.request.id", source.Request.RequestId.Value);
                activity.Activity.SetTag("kora.clipboard.capture.session.id", source.Request.SessionId.Value);
                activity.Activity.SetTag("kora.clipboard.capture.task.id", source.Request.TaskId.Value);
                var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
                    "clipboard.explain", SecurityAuditOutcome.Requested,
                    origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    source.SnapshotId.ToString("D"));
                audit.Write(requested);
                ClipboardExplanationResult result;
                var contextValidated = false;
                try
                {
                    var context = ClipboardExplanationContext.Create(source, request, time.GetUtcNow());
                    contextValidated = true;
                    var admittedTurn = await models.AdmitAsync(ModelTurnChoice.Local, context, null, admitted).ConfigureAwait(false);
                    if (!current()) { result = new(ClipboardExplanationOutcome.Cancelled); }
                    else if (admittedTurn.Turn is not { } turn)
                    { result = new(ClipboardExplanationOutcome.Unavailable, ModelReason: admittedTurn.Result.Reason); }
                    else
                    {
                        var response = await models.RunAsync(turn, admitted).ConfigureAwait(false);
                        if (!current()) { result = new(ClipboardExplanationOutcome.Cancelled); }
                        else if (response.Outcome == ModelTurnOutcome.Succeeded)
                        {
                            // ModelTurnHost owns provider identity and complete response-shape validation.
                            completedAnswer = response.Response!.Answer!;
                            completedExpiry = context.ExpiresAt;
                            result = new(ClipboardExplanationOutcome.Succeeded);
                        }
                        else { result = new(ClipboardExplanationOutcome.Unavailable, ModelReason: response.Reason); }
                    }
                }
                catch (ConversationCredentialException) when (!contextValidated)
                { result = new(ClipboardExplanationOutcome.CredentialRejected); }
                catch (InvalidDataException) when (!contextValidated)
                { result = new(ClipboardExplanationOutcome.EnvelopeRejected); }
                catch (OperationCanceledException) when (admitted.IsCancellationRequested)
                { result = new(ClipboardExplanationOutcome.Cancelled); }
                catch (Exception exception)
                {
                    Failure(logger, exception.GetType().Name);
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
                    activity.Complete(HostOperationOutcome.Unknown);
                    throw;
                }
                audit.Write(requested.WithOutcome(result.Outcome == ClipboardExplanationOutcome.Succeeded
                    ? SecurityAuditOutcome.Succeeded : result.Outcome == ClipboardExplanationOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed));
                activity.Complete(result.Outcome == ClipboardExplanationOutcome.Succeeded ? HostOperationOutcome.Completed
                    : result.Outcome == ClipboardExplanationOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                return Report(result);
            }, cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!Current()) { return Report(new(ClipboardExplanationOutcome.Cancelled)); }
                if (completed.Outcome == ClipboardExplanationOutcome.Succeeded)
                {
                    if (time.GetUtcNow() >= completedExpiry)
                    { return Report(new(ClipboardExplanationOutcome.Expired)); }
                    answeredSource = source;
                    answerEligible = eligible;
                    answer = completedAnswer;
                    answerExpiresAt = completedExpiry;
                }
            }
            return completed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return Report(new(ClipboardExplanationOutcome.Cancelled)); }
        finally
        {
            snapshots.Changed -= SourceChanged;
            lock (gate) { pending = null; }
            cancellation.Dispose();
            admittedCompletion.TrySetResult();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private ClipboardExplanationResult Report(ClipboardExplanationResult result)
    {
        Outcome(logger, result.Outcome, result.CaptureOutcome, result.ModelReason);
        return result;
    }

    private async Task<ClipboardExplanationResult?> EvaluateSourceUseAsync(
        ClipboardSnapshot source, Func<bool> current, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var request = activity.Request;
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "clipboard.source-use", SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            source.SnapshotId.ToString("D"));
        audit.Write(requested);
        try
        {
            var decision = await sourceUsePolicy.EvaluateAsync(source, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ClipboardExplanationResult? result = !current() ? new(ClipboardExplanationOutcome.Cancelled) : decision switch
            {
                ClipboardSourceUseOutcome.Accepted => null,
                ClipboardSourceUseOutcome.Denied => new(ClipboardExplanationOutcome.SourceUseDenied),
                ClipboardSourceUseOutcome.Unavailable => new(ClipboardExplanationOutcome.SourceUsePolicyUnavailable),
                _ => throw new InvalidDataException("The clipboard source-use policy returned an unknown decision."),
            };
            audit.Write(requested.WithOutcome(result is null ? SecurityAuditOutcome.Succeeded
                : result.Outcome == ClipboardExplanationOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied));
            activity.Complete(result is null ? HostOperationOutcome.Completed
                : result.Outcome == ClipboardExplanationOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            ++generation;
            RetireAnswer();
            pending?.Cancel();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RetireAnswer()
    {
        answer = null;
        answeredSource = null;
        answerEligible = null;
    }

    public async Task WaitForQuiescenceAsync()
    {
        Task outstanding;
        lock (gate) { outstanding = completion?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        await models.WaitForClipboardQuiescenceAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; }
        Clear();
    }
}