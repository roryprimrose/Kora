namespace Kora;

internal sealed partial class SessionsViewModel
{
    public Task SubmitClaudeConversationAsync() => SubmitConversationCoreAsync(allowQuestion: false, claude: true);
}
