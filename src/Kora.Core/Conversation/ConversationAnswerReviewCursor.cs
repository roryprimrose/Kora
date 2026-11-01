using Kora.Core.Storage;

namespace Kora.Core.Conversation;

public sealed record ConversationAnswerReviewCursor(SessionWorkspaceEntry Target,
    ConversationAnswerReviewFilter Filter, long HeadRevision, long ReviewRevision, long After);
