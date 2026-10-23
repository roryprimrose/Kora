using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public async Task<ConversationSubmissionReceipt> SubmitInstructionAsync(
        ScopedInstructionUseBroker.InstructionContextReview review, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        RequireOriginalSource(review.Origin);
        review.Consume();
        try
        {
            await review.ValidateSourceAsync(token).ConfigureAwait(false);
            List<ActivityLink> links = [];
            if (review.Trace is { } trace) { links.Add(new(trace)); }
            if (HostActivity.Current is { Activity: { } parent }) { links.Add(new(parent.Context)); }
            return await SubmitReviewedContextAsync(review.Target, review.Body, review.Capture,
                () => admitted() && review.IsCurrent(), review.ValidateSourceAsync, links,
                review.Origin, token, review.BeginExecution).ConfigureAwait(false);
        }
        finally { review.Retire(); }
    }
}
