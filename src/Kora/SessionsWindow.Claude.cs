namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindClaudeConversation(SessionsViewModel model) =>
        SubmitClaudeConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitClaudeConversationAsync());
}
