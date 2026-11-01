using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact retained-answer acknowledgements under the existing private history authority.</summary>
public interface ISessionAnswerReviewStore
{
    ValueTask<ConversationAnswerReviewPage> ReadAnswerReviewsAsync(SessionWorkspaceEntry target,
        ConversationAnswerReviewFilter filter, ConversationAnswerReviewCursor? cursor, int limit, CancellationToken token);
    ValueTask<ConversationMessage> ReadAnswerReviewMessageAsync(ConversationAnswerReviewPage page,
        ConversationAnswerReviewRow row, CancellationToken token);
    ValueTask<ConversationAnswerReviewRow> SetAnswerReviewedAsync(HostRequest control,
        ConversationAnswerReviewPage page, ConversationAnswerReviewRow row, bool reviewed,
        Func<bool> admitted, CancellationToken token);
}
