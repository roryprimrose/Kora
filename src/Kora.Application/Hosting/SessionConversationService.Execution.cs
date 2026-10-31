using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public event Func<ConversationAnswerPresentation, CancellationToken, ValueTask>? ProvisionalAnswerChanged;

    private async Task<ConversationSubmissionReceipt> ExecuteRetainedAsync(SessionWorkspaceEntry target,
        ConversationSubmissionReceipt retained, ModelTurnAdmission admission, ModelContextEnvelope context,
        Func<bool> live, Func<CancellationToken, Task>? validateSource, CancellationToken token,
        Func<ModelTurnResult, ModelTurnResult>? resolveResponse = null, Action<ModelTurnProvenance>? onAdmitted = null,
        Action? terminalCommitted = null, bool joinCancelledResources = false,
        LocalUnavailableContinuation? localHandoff = null, bool ordinaryActiveDeadline = false)
    {
        var executor = execution
            ?? throw new InvalidOperationException("The admitted model host execution port is not composed.");
        var turn = admission.Turn!;
        var request = retained.Message.Request;
        bool Current() => live() && time.GetUtcNow() >= context.CreatedAt && time.GetUtcNow() < context.ExpiresAt;
        if (admission.Result.Outcome != ModelTurnOutcome.Succeeded || admission.Result.Response is not null
            || admission.Result.Provenance != turn.Provenance || turn.Provenance.Request != request
            || !ReferenceEquals(turn.Context, context) || turn.SessionGeneration != target.Authority.Generation
            || turn.TaskRevision != retained.Task.Revision || turn.ConversationTarget is not null)
        { throw new InvalidDataException("Turn admission does not bind this exact original retained request."); }
        if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
        if (!Current() || !ProviderAdmitsSession(request.SessionId)
            || await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false) != target)
        { throw new InvalidOperationException("The original source or exact session changed before model execution."); }
        var runtime = executor as ModelTurnHost;
        await using var running = RegisterRunningConversation(target, turn, live, token);
        turn.PublishExecutionStep = running.PresentExecutionStep;
        turn.PublishAnswer = async (update, publicationToken) =>
        {
            bool PresentCurrent() => Current() && running.CanContinue && turn.IsRunning
                && !running.Token.IsCancellationRequested;
            publicationToken.ThrowIfCancellationRequested();
            if (!PresentCurrent())
            { throw new InvalidOperationException("The exact conversation no longer admits provisional output."); }
            var handlers = ProvisionalAnswerChanged;
            if (handlers is null) { return; }
            var presentation = new ConversationAnswerPresentation(running.Target, update, PresentCurrent);
            foreach (var handler in handlers.GetInvocationList()
                .Cast<Func<ConversationAnswerPresentation, CancellationToken, ValueTask>>())
            {
                publicationToken.ThrowIfCancellationRequested();
                if (!PresentCurrent())
                { throw new InvalidOperationException("Provisional presentation was revoked."); }
                await handler(presentation, publicationToken).ConfigureAwait(false);
            }
        };
        var confirmed = false;
        try
        {
            if (runtime is not null)
            {
                RunningConversationChanged?.Invoke(running.Status);
                ModelTurnReason resourceReason;
                try
                {
                    resourceReason = await runtime.ReserveConversationResourcesAsync(turn,
                        () => Current() && running.CanContinue, running.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (running.Token.IsCancellationRequested)
                { resourceReason = ModelTurnReason.CallerCancelled; }
                if (resourceReason != ModelTurnReason.None)
                {
                    running.BeginCompletion();
                    var cancelled = running.CancellationAccepted || resourceReason == ModelTurnReason.CallerCancelled;
                    var unavailable = await conversations.CompleteConversationSubmissionAsync(request,
                        cancelled ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Unavailable,
                        cancelled ? ModelTurnReason.CallerCancelled : resourceReason, live, token).ConfigureAwait(false);
                    terminalCommitted?.Invoke();
                    await RequireSubmissionReadbackAsync(unavailable, target, request, retained.Message.Body,
                        live, validateSource, token).ConfigureAwait(false);
                    confirmed = true;
                    running.Complete(unavailable.Message.Outcome!.Value, unavailable.Task.State);
                    return unavailable;
                }
                if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
            }
            token.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("Original source admission changed before host capture."); }
            onAdmitted?.Invoke(turn.Provenance);
            token.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("Original source admission changed during host capture."); }
            var dispatched = await conversations.BeginConversationExecutionAsync(request, turn.Provenance,
                target.Metadata?.Revision.Value ?? 0,
                () => Current() && running.CanContinue && (runtime is null || runtime.ConversationResourcesCurrent(turn)), token).ConfigureAwait(false);
            dispatched.Message.Validate();
            if (dispatched.Message.Request != request
                || !string.Equals(dispatched.Message.Body, retained.Message.Body, StringComparison.Ordinal)
                || dispatched.Message.Generation != target.Authority.Generation || dispatched.Message.Sequence != retained.Message.Sequence
                || dispatched.Message.Execution != turn.Provenance || dispatched.Message.Revision != 2
                || dispatched.Task.Request != request || dispatched.Task.State != HostTaskState.DispatchRecorded
                || dispatched.Task.Revision.Value != 2 || !live())
            { throw new IOException("Execution intent readback is unconfirmed; inspect without repeating the turn."); }
            turn.TaskRevision = dispatched.Task.Revision;
            turn.ConversationTarget = target;
            if (ordinaryActiveDeadline && runtime is not null)
            {
                runtime.CaptureOrdinaryActiveBudget(turn,
                    ordinaryDeadlines?.Capture() ?? Kora.Core.Configuration.OrdinaryTurnDeadline.Default);
            }
            var dispatchReadback = await conversations.ReadConversationMessageAsync(request.SessionId, request.RequestId, token).ConfigureAwait(false);
            if (dispatchReadback != dispatched.Message || !Current())
            { throw new IOException("The original dispatch source readback is unconfirmed; no model turn was repeated."); }
            running.Dispatched(dispatched.Task.Revision);
            RunningConversationChanged?.Invoke(running.Status);
            var result = await executor.RunAsync(turn, () => running.CanContinue, running.Token, validateSource).ConfigureAwait(false);
            if (!ReferenceEquals(turn.TerminalResult, result) || result.Provenance != turn.Provenance
                || !Enum.IsDefined(result.Outcome) || !Enum.IsDefined(result.Reason)
                || result.Outcome != ModelTurnOutcome.Succeeded && result.Response is not null)
            { throw new InvalidDataException("Only the owning admitted host's exact terminal result may complete this request."); }
            // This gate chooses one winner before any terminal transaction. A commit in progress cannot be rolled back by Cancel.
            if (running.BeginCompletion())
            {
                result = new(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, turn.Provenance);
            }
            // Logical timeout/cancellation is not physical completion. Caller shutdown retains its existing Unknown/recovery path.
            if (!token.IsCancellationRequested || joinCancelledResources)
            {
                if (runtime is not null) { await runtime.JoinConversationResourcesAsync(turn, CancellationToken.None).ConfigureAwait(false); }
                else { await executor.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            }
            await running.JoinCancellationAsync().ConfigureAwait(false);
            if (resolveResponse is not null && result.Outcome == ModelTurnOutcome.Succeeded)
            {
                result = resolveResponse(result);
                if (result is null || result.Provenance != turn.Provenance
                    || !Enum.IsDefined(result.Reason)
                    || result.Outcome is not (ModelTurnOutcome.Succeeded or ModelTurnOutcome.Unknown)
                    || result.Outcome != ModelTurnOutcome.Succeeded && result.Response is not null)
                { throw new InvalidDataException("A host source response projection cannot change turn authority or promote a failure."); }
            }
            ConversationResponse? answer = null;
            if (result.Outcome == ModelTurnOutcome.Succeeded)
            {
                try
                {
                    answer = ConversationResponse.FromResult(result, turn.Provenance);
                    (dispatched.Message with
                    {
                        Revision = 3, Outcome = result.Outcome, Reason = result.Reason,
                        AssistantResponse = answer, Observations = result.Observations,
                    }).Validate();
                }
                catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
                {
                    answer = null;
                    result = new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, turn.Provenance);
                }
            }
            if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            if (result.Outcome == ModelTurnOutcome.Succeeded && turn.OrdinaryActiveBudget?.Expired == true)
            {
                result = new(ModelTurnOutcome.TimedOut, ModelTurnReason.DeadlineExceeded, turn.Provenance);
                answer = null;
            }
            bool RetainCurrent() => live() && (result.Outcome != ModelTurnOutcome.Succeeded
                || Current() && turn.OrdinaryActiveBudget?.Expired != true
                    && (runtime is not null ? runtime.IsTurnQuiescent(turn) : turns.IsQuiescent));
            if (!RetainCurrent() || await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("The original source or session changed before terminal retention."); }
            if (result.Outcome == ModelTurnOutcome.Succeeded && turn.ValidateAnswerCommit is { } validateAnswer)
            {
                var reason = await validateAnswer(token).ConfigureAwait(false);
                if (reason != ModelTurnReason.None)
                {
                    result = new(ModelTurnOutcome.DeniedEgress, reason, turn.Provenance);
                    answer = null;
                }
            }
            var handoff = result.Outcome == ModelTurnOutcome.Unavailable && localHandoff is not null
                ? await localHandoff(turn, context, RetainCurrent, token).ConfigureAwait(false) : null;
            var completed = handoff?.Source
                ?? await conversations.CompleteConversationExecutionAsync(request, result, RetainCurrent, token).ConfigureAwait(false);
            if (completed.Message.Outcome != result.Outcome || completed.Message.Reason != result.Reason
                || completed.Message.AssistantResponse != answer || completed.Message.Observations != result.Observations
                || completed.Message.Execution != turn.Provenance
                || completed.Message.Generation != target.Authority.Generation || completed.Message.Sequence != retained.Message.Sequence
                || completed.Message.Revision != 3 || completed.Task.Revision.Value != 3)
            { throw new IOException("Assistant completion is unconfirmed; inspect the exact original request without retry."); }
            terminalCommitted?.Invoke();
            await RequireSubmissionReadbackAsync(completed, target, request, retained.Message.Body, live, validateSource, token).ConfigureAwait(false);
            confirmed = true;
            running.Complete(completed.Message.Outcome!.Value, completed.Task.State);
            return completed;
        }
        finally
        {
            turn.PublishExecutionStep = null;
            turn.PublishAnswer = null;
            // An accepted cancellation owns its join even when audit, privacy, storage or readback subsequently fails.
            try
            {
                if (runtime is not null && turn.ResourceLease is not null
                    && (!turn.ResourceDispatched || !token.IsCancellationRequested || running.CancellationAccepted || joinCancelledResources))
                {
                    await running.WaitForPhysicalResourcesAsync(
                        () => runtime.JoinConversationResourcesAsync(turn, CancellationToken.None)).ConfigureAwait(false);
                }
                else if (runtime is null && running.CancellationAccepted)
                {
                    await running.WaitForPhysicalResourcesAsync(
                        () => executor.WaitForQuiescenceAsync(CancellationToken.None)).ConfigureAwait(false);
                }
            }
            finally { ReleaseRunningConversation(running, confirmed); }
        }
    }

    private async Task RequireSubmissionReadbackAsync(ConversationSubmissionReceipt receipt, SessionWorkspaceEntry target,
        HostRequest request, string body, Func<bool> live, Func<CancellationToken, Task>? validateSource, CancellationToken token)
    {
        receipt.Message.Validate();
        var observed = await store.ReadTaskAsync(request.SessionId, request.TaskId, token).ConfigureAwait(false);
        var message = await conversations.ReadConversationMessageAsync(request.SessionId, request.RequestId, token).ConfigureAwait(false);
        if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
        var metadata = await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!string.Equals(receipt.Message.Body, body, StringComparison.Ordinal)
            || receipt.Message.Request != request || observed?.Task != receipt.Task
            || message != receipt.Message || !receipt.Task.IsTerminal || receipt.Task.State != receipt.Message.ExpectedTaskState
            || receipt.Task.Request != request || metadata != target || !live())
        { throw new IOException("Submission completion is unconfirmed; inspect exact retained content and task without retry."); }
    }
}