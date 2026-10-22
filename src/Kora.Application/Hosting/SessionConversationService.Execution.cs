using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private async Task<ConversationSubmissionReceipt> ExecuteRetainedAsync(SessionWorkspaceEntry target,
        ConversationSubmissionReceipt retained, ModelTurnAdmission admission, ModelContextEnvelope context,
        Func<bool> live, Func<CancellationToken, Task>? validateSource, CancellationToken token)
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
        if (!Current() || !turns.IsQuiescent || await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false) != target)
        { throw new InvalidOperationException("The original source or exact session changed before model execution."); }
        var dispatched = await conversations.BeginConversationExecutionAsync(request, turn.Provenance,
            target.Metadata?.Revision.Value ?? 0, Current, token).ConfigureAwait(false);
        dispatched.Message.Validate();
        if (dispatched.Message.Request != request
            || !string.Equals(dispatched.Message.Body, retained.Message.Body, StringComparison.Ordinal)
            || dispatched.Message.Generation != target.Authority.Generation || dispatched.Message.Sequence != retained.Message.Sequence
            || dispatched.Message.Execution != turn.Provenance || dispatched.Message.Revision != 2
            || dispatched.Task.Request != request || dispatched.Task.State != HostTaskState.DispatchRecorded
            || dispatched.Task.Revision.Value != 2 || !live())
        { throw new IOException("Execution intent readback is unconfirmed; inspect without repeating the turn."); }
        var dispatchReadback = await conversations.ReadConversationMessageAsync(request.SessionId, request.RequestId, token).ConfigureAwait(false);
        if (dispatchReadback != dispatched.Message || !Current())
        { throw new IOException("The original dispatch source readback is unconfirmed; no model turn was repeated."); }
        turn.TaskRevision = dispatched.Task.Revision;
        turn.ConversationTarget = target;
        await using var running = RegisterRunningConversation(target, turn, live, token);
        var confirmed = false;
        try
        {
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
            if (!token.IsCancellationRequested)
            { await executor.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            await running.JoinCancellationAsync().ConfigureAwait(false);
            ConversationResponse? answer = null;
            if (result.Outcome == ModelTurnOutcome.Succeeded)
            {
                try { answer = ConversationResponse.FromResult(result, turn.Provenance); }
                catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
                { result = new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, turn.Provenance); }
            }
            if (validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            bool RetainCurrent() => live() && (result.Outcome != ModelTurnOutcome.Succeeded || Current() && turns.IsQuiescent);
            if (!RetainCurrent() || await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("The original source or session changed before terminal retention."); }
            var completed = await conversations.CompleteConversationExecutionAsync(request, result, RetainCurrent, token).ConfigureAwait(false);
            if (completed.Message.Outcome != result.Outcome || completed.Message.Reason != result.Reason
                || completed.Message.AssistantResponse != answer || completed.Message.Execution != turn.Provenance
                || completed.Message.Generation != target.Authority.Generation || completed.Message.Sequence != retained.Message.Sequence
                || completed.Message.Revision != 3 || completed.Task.Revision.Value != 3)
            { throw new IOException("Assistant completion is unconfirmed; inspect the exact original request without retry."); }
            await RequireSubmissionReadbackAsync(completed, target, request, retained.Message.Body, live, validateSource, token).ConfigureAwait(false);
            confirmed = true;
            running.Complete(completed.Message.Outcome!.Value, completed.Task.State);
            return completed;
        }
        finally
        {
            // An accepted cancellation owns its join even when audit, privacy, storage or readback subsequently fails.
            if (running.CancellationAccepted) { await executor.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            ReleaseRunningConversation(running, confirmed);
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
