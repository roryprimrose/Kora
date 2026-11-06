using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

public sealed record ConversationAnswerReviewRow(HostId<RequestIdentity> RequestId, Guid TurnId,
    long Sequence, long MessageRevision, DateTimeOffset SubmittedAt, long ReviewRevision, bool MarkedReviewed);
