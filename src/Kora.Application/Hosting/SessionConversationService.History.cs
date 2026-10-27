using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Hosting.ModelHistoryContext;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public Task<ConversationHistoryReview> ReviewHistoryAsync(SessionWorkspaceEntry target, string body,
        IEnumerable<ConversationHistorySelection> selections, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi) =>
        ReviewHistoryCoreAsync(target, body, selections, admitted, origin, null, token);

    private async Task<ConversationHistoryReview> ReviewHistoryCoreAsync(SessionWorkspaceEntry target, string body,
        IEnumerable<ConversationHistorySelection> selections, Func<bool> admitted, RequestOrigin origin,
        ModelHistoryContextSource? advisorySource, CancellationToken token)
    {
        RequireOriginalSource(origin);
        var control = access.ControlRevision;
        bool Live() => !token.IsCancellationRequested && access.CanInspect && access.CanControl
            && access.ControlRevision == control && admitted() && advisorySource?.IsCurrent != false;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var ordered = ConversationHistoryContext.ValidateAndOrder(target.Authority.SessionId, selections);
            await workspace.ValidateConversationHistoryAsync(target, ordered, Live, token, advisorySource?.Message).ConfigureAwait(false);
            var now = time.GetUtcNow();
            var expires = now.AddSeconds(60);
            if (advisorySource is not null)
            {
                advisorySource.RequireOwner(this, reviewing: true);
                expires = advisorySource.ExpiresAt;
            }
            var review = new ConversationHistoryReview(this, target, body, ordered, origin, Live, time,
                now, expires, new(activity.Activity!.Context), advisorySource);
            activity.Complete(HostOperationOutcome.Completed);
            return review;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public async Task<ConversationSubmissionReceipt> SubmitHistoryAsync(ConversationHistoryReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        // Native confirmation of activated-voice input preserves its original channel.
        RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        var input = review.Consume(this);
        using (review)
        using (var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin), HostActivityLayer.Application, HostOperation.Request, [review.Cause]))
        {
            bool Live() => !token.IsCancellationRequested && admitted() && review.IsCurrent;
            Task Revalidate(CancellationToken cancellation) =>
                workspace.ValidateConversationHistoryAsync(review.Target, input.Selections, Live, cancellation,
                    review.AdvisorySource?.Message);
            ConversationSubmissionReceipt receipt;
            try
            {
                receipt = await SubmitReviewedContextAsync(review.Target, input.Body,
                    (request, now) => ConversationHistoryContext.CreateEnvelope(request, input.Body,
                        input.Selections, now, review.ExpiresAt < now.AddSeconds(15) ? review.ExpiresAt : now.AddSeconds(15)),
                    Live, Revalidate, [review.Cause], review.Origin, token).ConfigureAwait(false);
                activity.Complete(receipt.Message.Outcome switch
                {
                    ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
                    ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
                    ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
                    _ => HostOperationOutcome.Failed,
                });
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
            finally
            {
                if (review.AdvisorySource is not null && execution is not null)
                { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
            }
            return receipt;
        }
    }
}
