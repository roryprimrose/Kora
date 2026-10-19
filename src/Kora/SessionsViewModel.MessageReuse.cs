using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public bool CanReviewOriginalMessageReuse => CanReadConversation && access.CanControl
        && !conversationSourceHeld && reviewOriginalMessageReuse is not null;

    public Task ReviewOriginalMessageReuseAsync(ConversationMessage message)
    {
        if (!CanReviewOriginalMessageReuse) { return Task.CompletedTask; }
        return RunConversationAsync(async (target, live) =>
        {
            var page = conversationPage;
            var search = conversationSearchPage;
            var searchEpoch = conversationSearchEpoch;
            var fromPage = ConversationMessages.Any(row => ReferenceEquals(row, message));
            var fromSearch = ConversationSearchMessages.Any(row => ReferenceEquals(row, message));
            if (!fromPage && !fromSearch)
            { throw new InvalidOperationException("Review reuse requires an actual currently admitted original USER row, not a reconstructed record."); }
            bool Current() => live() && access.CanControl && !conversationSourceHeld
                && (fromPage ? ReferenceEquals(page, conversationPage)
                    : ReferenceEquals(search, conversationSearchPage) && searchEpoch == conversationSearchEpoch);
            var result = await (reviewOriginalMessageReuse
                ?? throw new InvalidOperationException("Original USER reuse unavailable: the native New request surface is not composed."))(
                    target, message, Current, lifetime.Token);
            if (!Current()) { throw new OperationCanceledException("Original USER reuse source presentation changed; no late callback was accepted."); }
            conversationStatus = result;
        });
    }
}
