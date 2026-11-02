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

    internal Task<ConversationDraftSnapshot> ReadConversationDraftForRecoveryAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            var control = access.ControlRevision;
            void RequireCurrent()
            {
                token.ThrowIfCancellationRequested();
                if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                    || request.SessionId != target.Authority.SessionId || !access.CanControl
                    || access.ControlRevision != control || !admitted())
                { throw new InvalidOperationException("Draft recovery requires unchanged exact-session native private control."); }
            }
            RequireCurrent();
            var before = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            RequireCurrent();
            if (before != target) { throw new InvalidOperationException("The exact draft recovery session source changed. Refresh explicitly."); }
            var draft = await Conversations.ReadConversationDraftAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var after = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            RequireCurrent();
            if (after != target) { throw new InvalidOperationException("The draft recovery session generation or metadata changed."); }
            draft.Draft.Validate();
            if (draft.Session != target.Authority || draft.Draft.SessionId != target.Authority.SessionId
                || draft.ExpectedTarget is { } expected && expected != target)
            { throw new InvalidDataException("Legacy recovery requires the exact session generation and saved source."); }
            return draft with { ExpectedTarget = target };
        }, token);

    public Task<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
        ConversationCursor? cursor, int limit, CancellationToken token) =>
        ReadAsync(() => Conversations.ReadConversationPageAsync(session, cursor, limit, token), token);

    public Task<AdmittedDetailContent> ReadConversationDetailAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token, bool assistant = false,
        Guid? observationId = null) =>
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
            var answer = assistant ? actual.AssistantResponse
                ?? throw new InvalidOperationException("This exact message has no retained assistant answer.") : null;
            var observation = observationId is { } id
                ? actual.Observations?.Items.SingleOrDefault(item => item.Id == id)
                    ?? throw new InvalidOperationException("This exact retained observation is unavailable; no replacement was queried.")
                : null;
            if (assistant && observation is not null)
            { throw new InvalidOperationException("Choose one exact retained source, not a combined source."); }
            var content = new AdmittedDetailContent(new(new(observation?.Id ?? answer?.Provenance.Turn.Value ?? actual.Request.RequestId.Value), actual.Revision),
                assistant ? DetailContentKind.Markdown : DetailContentKind.PlainText,
                observation is not null ? DetailContentOrigin.RetainedLocalObservation
                    : assistant ? DetailContentOrigin.RetainedAssistant : DetailContentOrigin.RetainedConversation,
                DetailSensitivity.DisclosureConfirmationRequired,
                observation is not null ? "Observed " + observation.CapabilityId
                    : assistant ? "Retained complete ASSISTANT answer" : "Retained original USER message",
                observation is not null ? "Original local host observation at " + observation.ObservedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                    + "; not current truth, execution, context, speech, export or clipboard authority."
                    : "Historical private immutable conversation source; not current execution, an approval or clipboard authority.",
                observation?.ResultJson ?? answer?.Body ?? actual.Body,
                conversationMessage: actual);
            var finalTarget = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var final = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!admitted() || finalTarget != target || final != actual)
            { throw new InvalidOperationException("Conversation detail source or private admission changed before publication."); }
            return content;
        }, token);

    public Task<ConversationMessage> ReadSpeechSummarySuggestionAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token) =>
        ReadSettingSuggestionAsync(target, expected, admitted, token);

    public Task<ConversationMessage> ReadSettingSuggestionAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            SessionConversationService.RequireOriginalSource(request.Origin);
            var control = access.ControlRevision;
            bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted();
            if (request.SessionId != target.Authority.SessionId || request.InvocationId is not null
                || !target.Authority.IsActive || expected.Request.SessionId != target.Authority.SessionId
                || expected.Generation != target.Authority.Generation || !Live())
            { throw new InvalidOperationException("Setting review requires exact Active-session original-user control."); }
            expected.Validate();
            var before = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            var after = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var final = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (before != target || after != target || actual != expected || final != actual || !Live())
            { throw new InvalidOperationException("The exact retained setting suggestion source changed; no newer answer was substituted."); }
            return actual;
        }, token);

}
