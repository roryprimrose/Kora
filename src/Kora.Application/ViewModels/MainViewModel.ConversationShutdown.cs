namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task<bool>>? conversationShutdown;

    public void BindConversationShutdown(Func<Task<bool>>? shutdown) => conversationShutdown = shutdown;
}
