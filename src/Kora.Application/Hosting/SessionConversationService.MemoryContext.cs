using Kora.Application.Memory;
using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal async Task<ConversationSubmissionReceipt> SubmitMemoryContextAsync(SessionMemoryContext workflow,
        SessionMemoryContextReview review, Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(review.Origin);
        using var use = workflow.Confirm(review, admitted, token);
        return await SubmitReviewedContextAsync(use.Target, use.Body, use.CreateEnvelope, use.Current,
            use.ValidateAsync, null, use.Origin, use.Token).ConfigureAwait(false);
    }
}
