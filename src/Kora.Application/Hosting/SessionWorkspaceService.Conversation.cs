using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
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

    public Task<AdmittedDetailContent> ReadConversationDetailAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                || request.SessionId != target.Authority.SessionId)
            { throw new InvalidOperationException("Conversation Details require host-resolved exact-session native presentation, not model authority."); }
            expected.Validate();
            if (!admitted() || expected.Request.SessionId != target.Authority.SessionId)
            { throw new InvalidOperationException("Select an exact retained record from this session before opening Details."); }
            var actualTarget = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            if (actualTarget != target || actual != expected)
            { throw new InvalidOperationException("The exact conversation record or session source changed. Refresh; no newer record was substituted."); }
            var content = new AdmittedDetailContent(new(new(actual.Request.RequestId.Value), actual.Revision),
                DetailContentKind.PlainText, DetailContentOrigin.RetainedConversation,
                DetailSensitivity.DisclosureConfirmationRequired, "Retained original USER message",
                "Private retained original USER text. Separate HOST status is not an assistant answer, execution permission or clipboard authority.",
                actual.Body, conversationMessage: actual);
            var finalTarget = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var final = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!admitted() || finalTarget != target || final != actual)
            { throw new InvalidOperationException("Conversation detail source or private admission changed before publication."); }
            return content;
        }, token);

    internal Task<ConversationDraftSnapshot> CommitConversationDraftAsync(ConversationDraftSnapshot expected,
        string? body, Func<bool> admitted, CancellationToken token)
    {
        InvalidateWorkOverview(expected.Session.SessionId);
        return ControlAsync(expected.Session.SessionId, RequestOrigin.LocalUi,
            (request, live) => Conversations.CommitConversationDraftAsync(request, expected, body, live, token),
            token, admitted, existingSubject: true, resolveTerminalReceipt: true);
    }
}
