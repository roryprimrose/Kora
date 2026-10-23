using System.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<ConversationSubmissionReceipt> SubmitSharedSkillAsync(SessionWorkspaceEntry target, string originalRequest,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, Func<bool> admitted,
        Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links,
        RequestOrigin origin, CancellationToken token) =>
        SubmitReviewedContextAsync(target, originalRequest, createContext, admitted, validateSource, links, origin, token);
}
