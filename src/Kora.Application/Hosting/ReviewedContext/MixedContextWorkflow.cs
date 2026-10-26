using Kora.Application.Memory;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.ReviewedContext;

/// <summary>Owns only this explicitly selected mixed review, not the independent source brokers.</summary>
internal sealed class MixedContextWorkflow(
    SessionConversationService conversation, SessionMemoryContext memories, ScopedInstructionUseBroker instructions,
    TimeProvider time) : IDisposable
{
    private MixedContextReview? current;
    private bool disposed;
    private long revision;
    private readonly Lock gate = new();

    internal async Task<MixedContextReview> ReviewAsync(SessionWorkspaceEntry target, string body,
        IEnumerable<ConversationHistorySelection> history, ArtifactDefinition instruction,
        IReadOnlyList<MemorySummary> selection, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        SessionConversationService.RequireOriginalSource(origin);
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
            source = await instructions.ReviewSourceAsync(target, instruction, Live, token, origin).ConfigureAwait(false);
            memory = await memories.ReviewAsync(target, selection, body, Live, token, origin).ConfigureAwait(false);
            var review = new MixedContextReview(this, source, memory, ordered, time, new(activity.Activity!.Context));
            lock (gate)
            {
                if (!Live()) { review.Dispose(); throw new OperationCanceledException("A newer mixed-context review replaced this operation."); }
                current = review;
            }
            await ValidateAsync(review, null, admitted, token).ConfigureAwait(false);
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

    internal async Task<ConversationSubmissionReceipt> ConfirmAsync(MixedContextReview review,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        SessionMemoryContextUse use;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!ReferenceEquals(current, review))
            { throw new InvalidOperationException("Only this workflow's current complete review may be confirmed."); }
            review.Consume(this);
            try { use = memories.Confirm(review.Memory, admitted, token); }
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
            memories.Retire();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Retire();
            memories.Dispose();
        }
    }
}
