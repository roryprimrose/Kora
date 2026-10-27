using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Conversation;

namespace Kora.Application.Diagnostics.EvidenceAnswers;

public interface IEvidenceAnswerConversation
{
    Task<ConversationSubmissionReceipt> SubmitAsync(EvidenceAnswerReview review, Func<bool> current,
        Func<CancellationToken, Task> validate, CancellationToken token);
    Task JoinAsync();
}
