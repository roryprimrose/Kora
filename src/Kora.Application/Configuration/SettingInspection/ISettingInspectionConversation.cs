using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Conversation;

namespace Kora.Application.Configuration.SettingInspection;

public interface ISettingInspectionConversation
{
    Task<ConversationSubmissionReceipt> SubmitAsync(SettingInspectionReview review, Func<bool> current,
        Func<CancellationToken, Task> validate, CancellationToken token);
    Task JoinAsync();
}
