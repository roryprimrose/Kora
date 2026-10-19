using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public Task<ConversationDraftSnapshot?> ReviewDraftRecoveryAsync(SessionWorkspaceEntry target,
        ConversationDraftSnapshot original, string raw,
        Func<ConversationDraftRecoveryReview, CancellationToken, Task<bool>> exactReview,
        Func<bool> current, CancellationToken token) =>
        ConversationDraftRecovery.ReviewAsync(target, original, raw,
            admitted => ReadDraftRecoverySourceAsync(target, current, admitted),
            exactReview, current, token);

    private async Task<ConversationDraftSnapshot> ReadDraftRecoverySourceAsync(SessionWorkspaceEntry target,
        Func<bool> current, CancellationToken token)
    {
        try { return await workspace.ReadConversationDraftForRecoveryAsync(target, current, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { throw new ConversationDraftRecoverySourceException(exception); }
    }
}
