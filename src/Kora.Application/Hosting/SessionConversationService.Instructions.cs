using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Application.Dependencies;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public async Task<ConversationSubmissionReceipt> SubmitInstructionAsync(
        ScopedInstructionUseBroker.InstructionContextReview review, Func<bool> admitted, CancellationToken token)
        => await SubmitInstructionAsync(review, admitted, null, token).ConfigureAwait(false);

    private async Task<ConversationSubmissionReceipt> SubmitInstructionAsync(
        ScopedInstructionUseBroker.InstructionContextReview review, Func<bool> admitted,
        PendingConversation? pending, CancellationToken token, LocalRequestProfileReview? localRequestProfile = null,
        Func<CancellationToken, Task>? validateOriginal = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        RequireOriginalSource(review.Origin);
        review.Consume();
        try
        {
            async Task Validate(CancellationToken cancellation)
            {
                if (validateOriginal is not null) { await validateOriginal(cancellation).ConfigureAwait(false); }
                await review.ValidateSourceAsync(cancellation).ConfigureAwait(false);
            }
            await Validate(token).ConfigureAwait(false);
            List<ActivityLink> links = [];
            if (review.Trace is { } trace) { links.Add(new(trace)); }
            if (HostActivity.Current is { Activity: { } parent }) { links.Add(new(parent.Context)); }
            return await SubmitReviewedContextAsync(review.Target, review.Body, review.Capture,
                () => admitted() && review.IsCurrent(), Validate, links,
                review.Origin, token, review.BeginExecution, pending: pending,
                joinCancelledResources: localRequestProfile is not null, localRequestProfile: localRequestProfile).ConfigureAwait(false);
        }
        finally { review.Retire(); }
    }
}
