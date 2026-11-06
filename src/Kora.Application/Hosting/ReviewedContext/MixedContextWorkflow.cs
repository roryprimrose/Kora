using Kora.Application.Memory;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.ReviewedContext;

/// <summary>Owns only this explicitly selected mixed review, not the independent source brokers.</summary>
internal sealed class MixedContextWorkflow(
    SessionConversationService conversation, SessionMemoryContext? memories, ScopedInstructionUseBroker? instructions,
    TimeProvider time) : IDisposable
{
    private MixedContextReview? current;
    private bool disposed;
    private long revision;
    private readonly Lock gate = new();
    private ConversationHistoryReview? reply;
    private CancellationTokenSource? replyCancellation;
    private CancellationTokenSource? replyExecution;

    internal async Task<ConversationHistoryReview> ReviewReplyAsync(SessionWorkspaceEntry target, string body,
        ConversationMessage source, Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(RequestOrigin.LocalUi);
        long version;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Retire();
            if (replyExecution is not null)
            { throw new InvalidOperationException("The previous reply still owns provider resources; wait for its actual completion."); }
            version = revision;
        }
        bool Live() => !Volatile.Read(ref disposed) && Interlocked.Read(ref revision) == version && admitted();
        var now = time.GetUtcNow();
        _ = MixedConversationContext.CreateReplyEnvelope(
            new(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            body, source, now, now.AddSeconds(60));
        var review = await conversation.ReviewHistoryAsync(target, body,
            [new(source, ConversationHistoryRole.Assistant)], Live, token).ConfigureAwait(false);
        lock (gate)
        {
            if (!Live())
            { review.Dispose(); throw new OperationCanceledException("The exact reply source or input changed during review."); }
            replyCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            reply = review;
        }
        return review;
    }

    internal async Task<ConversationSubmissionReceipt> ConfirmReplyAsync(ConversationHistoryReview review,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        CancellationTokenSource execution;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!ReferenceEquals(reply, review) || replyCancellation is null)
            { throw new InvalidOperationException("Review this workflow's exact answer and unchanged follow-up first; confirmation is single-use."); }
            execution = replyCancellation;
            replyExecution = execution;
            replyCancellation = null;
            reply = null;
        }
        using (review)
        using (execution)
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(execution.Token, token))
        {
            try
            {
                return await conversation.SubmitHistoryAsync(review, admitted, cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (gate) { replyExecution = null; }
            }
        }
    }

    internal async Task<MixedContextReview> ReviewAsync(SessionWorkspaceEntry target, string body,
        IEnumerable<ConversationHistorySelection> history, ArtifactDefinition instruction,
        IReadOnlyList<MemorySummary> selection, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        SessionConversationService.RequireOriginalSource(origin);
        var memoryBroker = memories ?? throw new InvalidOperationException("This reply workflow has no memory selection.");
        var instructionBroker = instructions ?? throw new InvalidOperationException("This reply workflow has no instruction selection.");
        long version;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Retire();
            version = revision;
        }
        bool Live() => !Volatile.Read(ref disposed) && Interlocked.Read(ref revision) == version && admitted();
        ConversationText.Validate(body, submission: true);
        var ordered = ConversationHistoryContext.ValidateAndOrder(target.Authority.SessionId, history);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        ScopedInstructionUseBroker.InstructionSourceReview? source = null;
        SessionMemoryContextReview? memory = null;
        try
        {
            await conversation.ValidateMixedHistoryAsync(target, ordered, Live, token).ConfigureAwait(false);
            source = await instructionBroker.ReviewSourceAsync(target, instruction, Live, token, origin).ConfigureAwait(false);
            memory = await memoryBroker.ReviewMixedSourceAsync(target, selection, body, Live, origin, token).ConfigureAwait(false);
            var review = new MixedContextReview(this, source, memory, ordered, time, new(activity.Activity!.Context));
            lock (gate)
            {
                if (!review.Current) { review.Dispose(); throw new OperationCanceledException("A newer mixed-context review replaced this operation."); }
                current = review;
            }
            await ValidateAsync(review, null, admitted, token).ConfigureAwait(false);
            if (!review.Current)
            { review.Dispose(); throw new OperationCanceledException("The original review was revoked before publication."); }
            review.MarkValidated();
            activity.Complete(HostOperationOutcome.Completed);
            return review;
        }
        catch (OperationCanceledException)
        {
            source?.Retire();
            memory?.Dispose();
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            source?.Retire();
            memory?.Dispose();
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    internal async Task<MixedContextReview> RemoveWholeContributionAsync(MixedContextReview review,
        MixedContextContribution contribution, Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(review.Origin);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin), HostActivityLayer.Application, HostOperation.Request, [review.Cause]);
        try
        {
            MixedContextReview replacement;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!ReferenceEquals(current, review))
                { throw new InvalidOperationException("Only the exact current review may be reduced."); }
                replacement = review.RemoveWholeContribution(contribution, time, new(activity.Activity!.Context));
                current = replacement;
            }
            await ValidateAsync(replacement, null, admitted, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!admitted() || !replacement.Current)
                { replacement.Dispose(); throw new OperationCanceledException("The reduced review is no longer current; read fresh sources."); }
                replacement.MarkValidated();
            }
            activity.Complete(HostOperationOutcome.Completed);
            return replacement;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    internal async Task<ConversationSubmissionReceipt> ConfirmAsync(MixedContextReview review,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        var memoryBroker = memories ?? throw new InvalidOperationException("This reply workflow has no mixed memory source.");
        SessionMemoryContextUse use;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!ReferenceEquals(current, review))
            { throw new InvalidOperationException("Only this workflow's current complete review may be confirmed."); }
            review.Consume(this);
            try { use = memoryBroker.Confirm(review.Memory, admitted, token); }
            catch { review.Dispose(); throw; }
        }
        using (review)
        using (use)
        {
            return await conversation.SubmitMixedContextAsync(review, admitted,
                cancellation => ValidateAsync(review, use, admitted, cancellation), use.Token).ConfigureAwait(false);
        }
    }

    private async Task ValidateAsync(MixedContextReview review, SessionMemoryContextUse? use,
        Func<bool> admitted, CancellationToken token)
    {
        bool Live() => !disposed && admitted() && review.Current;
        try
        {
            token.ThrowIfCancellationRequested();
            await conversation.ValidateMixedHistoryAsync(review.Target, review.History, Live, token).ConfigureAwait(false);
            await review.Instruction.ValidateSourceAsync(token).ConfigureAwait(false);
            if (use is not null) { await use.ValidateAsync(token).ConfigureAwait(false); }
            else { await review.Memory.ValidateAsync(Live, token).ConfigureAwait(false); }
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    internal void Retire()
    {
        lock (gate)
        {
            revision++;
            current?.Dispose();
            current = null;
            reply?.Dispose();
            reply = null;
            replyCancellation?.Cancel();
            replyCancellation?.Dispose();
            replyCancellation = null;
            replyExecution?.Cancel();
            memories?.Retire();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Retire();
            memories?.Dispose();
        }
    }
}
