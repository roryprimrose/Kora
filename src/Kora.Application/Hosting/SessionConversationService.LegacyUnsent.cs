using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public Task<ConversationDraftSnapshot> ReadLegacyUnsentAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token) =>
        workspace.ReadConversationDraftForRecoveryAsync(target, admitted, token);
}
