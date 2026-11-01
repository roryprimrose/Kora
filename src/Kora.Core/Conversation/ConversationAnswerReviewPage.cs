using System.Collections.Immutable;

using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>A bounded, metadata-only observation of one exact conversation. New results require a fresh read.</summary>
public sealed record ConversationAnswerReviewPage(SessionWorkspaceEntry Target,
    ConversationAnswerReviewFilter Filter, long HeadRevision, long ReviewRevision,
    int TotalAnswers, int NotMarkedReviewed, ImmutableArray<ConversationAnswerReviewRow> Rows,
    ConversationAnswerReviewCursor? Next);
