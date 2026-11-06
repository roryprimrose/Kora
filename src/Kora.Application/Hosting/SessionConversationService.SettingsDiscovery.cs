using System.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<ConversationSubmissionReceipt> SubmitSettingsDiscoveryAsync(SessionWorkspaceEntry target,
        SettingInspectionReview review, Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext,
        Func<bool> admitted, Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links,
        Func<ModelTurnResult, ModelTurnResult> resolveResponse, CancellationToken token)
    {
        if (execution is not ModelTurnHost host || review.ToolMode != ModelToolMode.SettingsDiscovery
            || target.Authority.SessionId != review.Destination.SessionId)
        { throw new InvalidOperationException("The canonical reviewed settings discovery host is unavailable."); }
        void Bind(ModelTurn turn, Func<bool> _) => host.BindSettingsDiscovery(turn, review);
        return SubmitCoreAsync(target, review.Question, admitted, validateSource, links, review.Original.Origin,
            createContext, ModelTurnChoice.Local, token, resolveResponse: resolveResponse, bindWebPage: Bind,
            joinCancelledResources: true);
    }
}
