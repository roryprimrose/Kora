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
        ConversationSubmissionReceipt? completed = null;
        try
        {
            return await workflow.Submit(review,
                async (createContext, live, validate, resolve, cancellation) =>
                {
                    ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
                    completed = await SubmitReviewedContextAsync(review.Target, review.Question, createContext,
                        live, validate, links, review.Origin, cancellation, onAdmitted: null,
                        resolveResponse: resolve).ConfigureAwait(false);
                    return completed;
                }, request, admitted, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (completed is not null
            && exception is not (OutOfMemoryException or ConversationSubmissionUnconfirmedException))
        {
            throw new ConversationSubmissionUnconfirmedException(completed.Message.Request, exception);
        }
    }
}
