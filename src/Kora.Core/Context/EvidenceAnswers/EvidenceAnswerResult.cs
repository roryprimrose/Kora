using Kora.Core.Conversation;

namespace Kora.Core.Context.EvidenceAnswers;

public sealed record EvidenceAnswerResult(EvidenceAnswerOutcome Outcome, ConversationSubmissionReceipt? Receipt = null);
