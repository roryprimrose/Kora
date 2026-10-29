namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindClaudeConversation(SessionsViewModel model)
    {
        SubmitClaudeConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitClaudeConversationAsync());
        SubmitClaudeAnswerStream.Click += async (_, _) => await (ConversationOperation = model.SubmitClaudeAnswerStreamAsync());
        SubmitClaudeObservations.Click += async (_, _) => await (ConversationOperation = model.SubmitClaudeObservationsAsync());
        SubmitClaudeWithClarification.Click += async (_, _) => await (ConversationOperation = model.SubmitClaudeWithClarificationAsync());
    }
}
