using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.McpResultFollowUp;

internal sealed class McpResultFollowUpWorkflow(
    SessionConversationService conversation, IMcpResultSourceUse sourceUse, TimeProvider time)
{
    private readonly Lock gate = new();
    private McpResultFollowUpReview? current;
    private Task<ConversationSubmissionReceipt>? running;
    private Task<McpResultFollowUpReview>? reviewing;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? reviewCancellation;
    private bool held;
    private long revision;

    internal bool Held { get { lock (gate) { return held; } } }
    internal bool IsRunning { get { lock (gate) { return reviewing is { IsCompleted: false } || running is { IsCompleted: false }; } } }

    internal Task<McpResultFollowUpReview> ReviewAsync(McpResultObservation observation, string question,
        Func<bool> admitted, CancellationToken token, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        SessionConversationService.RequireOriginalSource(origin);
        lock (gate)
        {
            if (held || reviewing is { IsCompleted: false } || running is { IsCompleted: false })
            { throw new InvalidOperationException("The previous question remains held or still owns provider resources; do not retry."); }
            current?.Dispose();
            current = null;
            running = null;
            var owned = CancellationTokenSource.CreateLinkedTokenSource(token);
            reviewCancellation = owned;
            reviewing = ReviewAndJoinAsync(observation, question, admitted, origin, ++revision, owned);
            return reviewing;
        }
    }

    private async Task<McpResultFollowUpReview> ReviewAndJoinAsync(McpResultObservation observation, string question,
        Func<bool> admitted, RequestOrigin origin, long version, CancellationTokenSource owned)
    {
        try { return await ReviewCoreAsync(observation, question, admitted, origin, version, owned.Token).ConfigureAwait(false); }
        finally
        {
            lock (gate) { reviewCancellation = null; }
            owned.Dispose();
        }
    }

    private async Task<McpResultFollowUpReview> ReviewCoreAsync(McpResultObservation observation, string question,
        Func<bool> admitted, RequestOrigin origin, long version, CancellationToken token)
    {
        bool Live() => !token.IsCancellationRequested && admitted() && Interlocked.Read(ref revision) == version;
        var task = await conversation.ValidateMcpObservationAsync(observation, Live, token).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var expires = observation.Invocation.ExpiresAt < now.AddSeconds(60) ? observation.Invocation.ExpiresAt : now.AddSeconds(60);
        var review = new McpResultFollowUpReview(this, observation, task, question, origin,
            sourceUse.Check(observation, origin), now, expires);
        try
        {
            await ValidateAsync(review, Live, token, requireQualified: false).ConfigureAwait(false);
            lock (gate)
            {
                if (!Live()) { throw new OperationCanceledException("A newer observation review retired this source."); }
                current = review;
            }
            return review;
        }
        catch { review.Dispose(); throw; }
    }

    internal Task<ConversationSubmissionReceipt> ConfirmAsync(McpResultFollowUpReview review,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(review.Origin);
        lock (gate)
        {
            if (held || !ReferenceEquals(current, review) || running is { IsCompleted: false })
            { throw new InvalidOperationException("Only this workflow's current observation review may be confirmed once."); }
            review.Consume(this);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            running = SubmitAsync(review, admitted, cancellation);
            return running;
        }
    }

    private async Task<ConversationSubmissionReceipt> SubmitAsync(McpResultFollowUpReview review,
        Func<bool> admitted, CancellationTokenSource owned)
    {
        try
        {
            await ValidateAsync(review, admitted, owned.Token).ConfigureAwait(false);
            return await conversation.SubmitMcpResultFollowUpAsync(review,
                () => Current(review, admitted), check => ValidateAsync(review, admitted, check), owned.Token).ConfigureAwait(false);
        }
        catch (ConversationSubmissionUnconfirmedException)
        {
            lock (gate) { held = true; }
            throw;
        }
        finally
        {
            // The canonical executor can return cancellation before an ignoring provider terminates.
            try { await conversation.JoinMcpResultFollowUpAsync().ConfigureAwait(false); }
            finally
            {
                lock (gate)
                {
                    review.Dispose();
                    if (ReferenceEquals(cancellation, owned)) { cancellation = null; }
                }
                owned.Dispose();
            }
        }
    }

    private bool Current(McpResultFollowUpReview review, Func<bool> admitted) =>
        review.Current && admitted() && time.GetUtcNow() >= review.CreatedAt && time.GetUtcNow() < review.ExpiresAt
        && sourceUse.Check(review.Observation, review.Origin) == review.Decision;

    private async Task ValidateAsync(McpResultFollowUpReview review, Func<bool> admitted,
        CancellationToken token, bool requireQualified = true)
    {
        token.ThrowIfCancellationRequested();
        if (!Current(review, admitted)
            || requireQualified && review.Decision.Kind != McpResultSourceUseKind.QualifiedLocalOnly)
        { throw new InvalidOperationException("The exact observation, classification, source channel or independent source-use qualification is unavailable."); }
        var task = await conversation.ValidateMcpObservationAsync(review.Observation,
            () => Current(review, admitted), token).ConfigureAwait(false);
        if (task != review.SourceTask || !Current(review, admitted))
        { throw new InvalidOperationException("The completed observation's exact host receipt changed."); }
    }

    internal async Task CancelAsync()
    {
        Task? work;
        Task? sourceWork;
        Task stop;
        Task stopSource;
        lock (gate)
        {
            current?.Dispose();
            current = null;
            revision++;
            work = running;
            sourceWork = reviewing;
            stop = cancellation?.CancelAsync() ?? Task.CompletedTask;
            stopSource = reviewCancellation?.CancelAsync() ?? Task.CompletedTask;
        }
        await stop.ConfigureAwait(false);
        await stopSource.ConfigureAwait(false);
        if (work is not null)
        {
#pragma warning disable VSTHRD003 // Joins the exact retained provider/source operation; cancellation is not physical completion.
            await work.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        if (sourceWork is not null)
        {
#pragma warning disable VSTHRD003 // Joins the exact local source validation task even if its store ignores cancellation.
            await sourceWork.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
    }
}
