using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionConversationStore Conversations => store as ISessionConversationStore
        ?? throw new InvalidOperationException("The consolidated private conversation authority is unavailable.");

    public Task<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => Conversations.ReadConversationDraftAsync(session, token), token);

    public Task<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
        ConversationCursor? cursor, int limit, CancellationToken token) =>
        ReadAsync(() => Conversations.ReadConversationPageAsync(session, cursor, limit, token), token);

    internal Task<ConversationDraftSnapshot> CommitConversationDraftAsync(ConversationDraftSnapshot expected,
        string? body, Func<bool> admitted, CancellationToken token)
    {
        InvalidateWorkOverview(expected.Session.SessionId);
        return ControlAsync(expected.Session.SessionId, RequestOrigin.LocalUi,
            (request, live) => Conversations.CommitConversationDraftAsync(request, expected, body, live, token),
            token, admitted, existingSubject: true, resolveTerminalReceipt: true);
    }
}
