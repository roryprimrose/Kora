namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task>? knowledgeSourcesShutdown;

    public void BindKnowledgeSourcesShutdown(Func<Task>? shutdown) => knowledgeSourcesShutdown = shutdown;

    private async Task JoinKnowledgeSourcesAsync()
    {
        if (knowledgeSourcesShutdown is { } shutdown) { await shutdown().ConfigureAwait(false); }
    }
}
