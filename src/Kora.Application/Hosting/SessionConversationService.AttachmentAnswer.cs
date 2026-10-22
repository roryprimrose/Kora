using System.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public Task<SessionAttachmentAnswerReview> PrepareAttachmentAnswerAsync(SessionWorkspaceEntry target,
        SessionFileAttachment expected, string question, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi) =>
        (attachmentAnswers ?? throw new InvalidOperationException("Retained attachment answering is unavailable."))
            .Prepare(target, expected, question, admitted, token, origin);

    public async Task<ConversationSubmissionReceipt> SubmitAttachmentAnswerAsync(SessionAttachmentAnswerReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        var workflow = attachmentAnswers
            ?? throw new InvalidOperationException("Retained attachment answering is unavailable.");
        var request = new HostRequest(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin);
        var submissionEntered = false;
        try
        {
            return await workflow.Submit(review,
                (createContext, live, validate, resolve, cancellation) =>
                {
                    ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
                    submissionEntered = true;
                    return SubmitReviewedContextAsync(review.Target, review.Question, (original, now) =>
                    {
                        request = original;
                        return createContext(original, now);
                    }, live, validate, links, review.Origin, cancellation, resolveResponse: resolve);
                }, request, admitted, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (submissionEntered
            && exception is not (OutOfMemoryException or ConversationSubmissionUnconfirmedException or ConversationCapacityException))
        {
            throw new ConversationSubmissionUnconfirmedException(request, exception);
        }
    }

}
