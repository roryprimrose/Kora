using System.Diagnostics;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

/// <summary>One original local operation, exact reviewed atomic successor, and independently reviewed hosted execution.</summary>
public sealed class VerifiedLocalHandoffOperation(ModelProviderHandoffWorkflow workflow, ModelTurnHost host,
    SessionConversationService conversation, ISessionConversationStore store, ISessionWorkspaceStore workspace,
    TimeProvider time)
{
    public event Action? ReviewRequested;

    internal sealed class State(TaskCompletionSource<ConversationSubmissionReceipt> completion, CancellationToken token) : IAsyncDisposable
    {
        private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        private readonly Lock gate = new();
        private Task cancellation = Task.CompletedTask;
        internal TaskCompletionSource<ModelHandoffResult> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ConversationSubmissionReceipt> Completion { get; } = completion;
        internal CancellationToken Token => lifetime.Token;
        internal ModelHandoffOffer? Offer { get; set; }
        internal void Cancel()
        {
            lock (gate)
            {
                if (!Completion.Task.IsCompleted && !lifetime.IsCancellationRequested)
                { cancellation = lifetime.CancelAsync(); }
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { await cancellation.WaitAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { lifetime.Dispose(); }
        }
    }

    public async Task<ConversationSubmissionReceipt> RunAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, CancellationToken token)
    {
        var completion = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var state = new State(completion, token);
        ModelHandoffOffer? offered = null;
        ModelContextEnvelope? successorContext = null;
        ConversationHandoffCommit? committed = null;
        ModelTurn? successorTurn = null;
        ActivityLink sourceLink = default;
        try
        {
            async Task<ConversationHandoffCommit?> AfterLocal(ModelTurn turn, ModelContextEnvelope context,
                Func<bool> live, CancellationToken sourceToken)
            {
                var result = await workflow.OfferAfterLocalAsync(turn, context, state, sourceToken).ConfigureAwait(false);
                if (result.Offer is not { LocalUnavailable: { } observation } offer)
                {
                    if (state.Offer is { } unpublished)
                    { await workflow.ClosePendingAsync(unpublished, CancellationToken.None).ConfigureAwait(false); }
                    return null;
                }
                offered = offer;
                state.Offer = offer;
                ReviewRequested?.Invoke();
                var remaining = context.ExpiresAt - time.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    await workflow.ClosePendingAsync(offer, CancellationToken.None).ConfigureAwait(false);
                    return null;
                }
                using var expiry = new CancellationTokenSource(remaining, time);
                using var reviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(sourceToken, state.Token, expiry.Token);
                ModelHandoffResult reviewed;
                try { reviewed = await state.Decision.Task.WaitAsync(reviewLifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!sourceToken.IsCancellationRequested)
                {
                    await workflow.ClosePendingAsync(state.Offer!, CancellationToken.None).ConfigureAwait(false);
                    return null;
                }
                if (reviewed is not { Outcome: ModelHandoffOutcome.Approved, Review: { } review }
                    || review.Offer.LocalUnavailable != observation
                    || Interlocked.Exchange(ref review.Used, 1) != 0)
                {
                    await workflow.ClosePendingAsync(state.Offer!, sourceToken).ConfigureAwait(false);
                    return null;
                }
                offer = review.Offer;
                offered = offer;
                var checkedOffer = await host.CheckHandoffAsync(offer, reviewLifetime.Token).ConfigureAwait(false);
                if (checkedOffer.Reason != ModelTurnReason.None)
                {
                    await workflow.ClosePendingAsync(offer, sourceToken).ConfigureAwait(false);
                    return null;
                }
                var request = new HostRequest(new(Guid.NewGuid()), context.Request.SessionId,
                    new(Guid.NewGuid()), context.Request.Origin);
                successorContext = Rebind(offer.Context, request);
                sourceLink = new(HostActivity.RequireCurrent().Activity!.Context);
                committed = await store.CommitVerifiedLocalHandoffAsync(observation, offer.Question,
                    offer.Context, successorContext, offer.Target!.Profile, offer.Target.Selection, offer.Target.Session,
                    () => live() && admitted() && offer.Target.Current() && !reviewLifetime.IsCancellationRequested,
                    reviewLifetime.Token).ConfigureAwait(false);
                return committed;
            }

            var source = await conversation.SubmitLocalFirstAsync(target, body,
                () => admitted() && !token.IsCancellationRequested, AfterLocal, token).ConfigureAwait(false);
            if (committed is null)
            {
                state.Completion.TrySetResult(source);
                return source;
            }
            var context = successorContext!;
            var successor = committed.Successor;
            using var activity = HostActivity.BeginRoot(context.Request, HostActivityLayer.Application,
                HostOperation.Request, [sourceLink]);
            bool Live() => admitted() && !state.Token.IsCancellationRequested;
            if (!Live() || await workspace.ReadMetadataAsync(context.Request.SessionId, state.Token).ConfigureAwait(false) != target
                || await store.ReadConversationMessageAsync(context.Request.SessionId, context.Request.RequestId,
                    state.Token).ConfigureAwait(false) != successor.Message)
            { throw new InvalidOperationException("The exact committed handoff successor or original private session changed."); }
            var admission = await host.AdmitVerifiedHandoffAsync(offered!, committed, context, state.Token).ConfigureAwait(false);
            successorTurn = admission.Turn;
            var completed = await conversation.ExecuteHandoffSuccessorAsync(target, successor, admission,
                context, Live, state.Token).ConfigureAwait(false);
            activity.Complete(completed.Message.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                : completed.Message.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled
                : completed.Message.Outcome == ModelTurnOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
            state.Completion.TrySetResult(completed);
            return completed;
        }
        catch (Exception exception)
        {
            var failure = exception;
            if (successorTurn?.ResourceLease is not null)
            {
                try { await host.JoinConversationResourcesAsync(successorTurn, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception joinFailure)
                {
                    failure = new AggregateException("The exact handoff successor failed and its resource join is unconfirmed.",
                        exception, joinFailure);
                }
            }
            state.Completion.TrySetException(failure);
        }
        finally
        {
            if (state.Offer is { } last) { workflow.Retire(last); }
        }
        return await completion.Task.ConfigureAwait(false);
    }

    private static ModelContextEnvelope Rebind(ModelContextEnvelope context, HostRequest request) =>
        new(request, context.SystemPolicy, context.UserRequest,
            context.Evidence.Select(item => item with { Request = request }), context.CreatedAt, context.ExpiresAt,
            context.ToolMode, context.Constraints, context.McpTool, context.WebPages);
}
