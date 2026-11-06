using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionAnswerReviewStore AnswerReviews => store as ISessionAnswerReviewStore
        ?? throw new InvalidOperationException("The authoritative answer-review store is unavailable.");

    public Task<ConversationAnswerReviewPage> ReadAnswerReviewsAsync(SessionWorkspaceEntry target,
        ConversationAnswerReviewFilter filter, ConversationAnswerReviewCursor? cursor, int limit,
        Func<bool> admitted, CancellationToken token) => ReadAsync(async () =>
    {
        RequireAnswerReviewInput(target, admitted);
        var page = await AnswerReviews.ReadAnswerReviewsAsync(target, filter, cursor, limit, token).ConfigureAwait(false);
        RequireAnswerReviewInput(target, admitted);
        return page;
    }, token);

    public Task<AdmittedDetailContent> ReadAnswerReviewDetailAsync(ConversationAnswerReviewPage page,
        ConversationAnswerReviewRow row, Func<bool> admitted, CancellationToken token) => ReadAsync(async () =>
    {
        RequireAnswerReviewInput(page.Target, admitted);
        var message = await AnswerReviews.ReadAnswerReviewMessageAsync(page, row, token).ConfigureAwait(false);
        var detail = await ReadConversationDetailAsync(page.Target, message, admitted, token, assistant: true).ConfigureAwait(false);
        _ = await AnswerReviews.ReadAnswerReviewMessageAsync(page, row, token).ConfigureAwait(false);
        RequireAnswerReviewInput(page.Target, admitted);
        return detail;
    }, token);

    public async Task<ConversationAnswerReviewRow> SetAnswerReviewedAsync(ConversationAnswerReviewPage page,
        ConversationAnswerReviewRow row, bool reviewed, Func<bool> admitted, CancellationToken token)
    {
        RequireAnswerReviewInput(page.Target, admitted);
        var original = HostActivity.RequireCurrent().Request;
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted();
        _ = await ReadAsync(() => AnswerReviews.ReadAnswerReviewMessageAsync(page, row, token), token).ConfigureAwait(false);
        RequireAnswerReviewInput(page.Target, Live);
        return await ControlAsync(page.Target.Authority.SessionId, original.Origin,
            (request, eligible) => AnswerReviews.SetAnswerReviewedAsync(request, page, row, reviewed, eligible, token),
            token, Live, existingSubject: true, terminalCommitted: true, originalRequest: original,
            finalizeIncompleteOnFailure: true).ConfigureAwait(false);
    }

    private static void RequireAnswerReviewInput(SessionWorkspaceEntry target, Func<bool> admitted)
    {
        var request = HostActivity.RequireCurrent().Request;
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
            || request.SessionId != target.Authority.SessionId || !admitted())
        { throw new InvalidOperationException("Answer-review inspection requires the original exact-session private native input and unchanged presentation."); }
    }
}
