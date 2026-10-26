using Kora.Application.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal ConversationQuestionPresenter? QuestionPresenter { get; set; }
    public bool CanSubmitConversationWithClarification => CanSubmitConversation && QuestionPresenter is not null;
    public Task SubmitConversationWithClarificationAsync() => SubmitConversationCoreAsync(allowQuestion: true);
    public Task SubmitReadOnlyWithClarificationAsync() =>
        SubmitConversationCoreAsync(allowQuestion: true, clarificationTools: Kora.Core.Dependencies.ModelToolMode.ReadOnly);
}
