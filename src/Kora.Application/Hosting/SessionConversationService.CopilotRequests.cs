using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<ConversationSubmissionReceipt> SubmitReviewedCopilotAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, Func<CancellationToken, Task> beforeRetain,
        IEnumerable<ActivityLink> links, RequestOrigin origin, CancellationToken token) =>
        SubmitCoreAsync(target, body, admitted, beforeRetain, links, origin, null,
            ModelTurnChoice.Hosted, token, copilot: true, ordinaryActiveDeadline: true);
}
