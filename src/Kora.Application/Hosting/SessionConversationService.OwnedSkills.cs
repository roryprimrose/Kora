using System.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal async Task<ConversationSubmissionReceipt> SubmitOwnedSkillAsync(SessionWorkspaceEntry target, string originalRequest,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, Func<bool> admitted,
        Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links, RequestOrigin origin, CancellationToken token)
    {
        try
        {
            return await SubmitReviewedContextAsync(target, originalRequest, createContext, admitted,
                validateSource, links, origin, token).ConfigureAwait(false);
        }
        finally
        {
            // Cancellation is not proof that the admitted provider has physically stopped.
            if (execution is not null) { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
