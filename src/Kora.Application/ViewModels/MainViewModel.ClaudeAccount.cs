namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task<bool>> claudeShutdown = static () => Task.FromResult(true);
    public event EventHandler? ClaudeAccountSetupRequested;
    public void ConfigureClaudeAccount() => ClaudeAccountSetupRequested?.Invoke(this, EventArgs.Empty);
    public void BindClaudeShutdown(Func<Task<bool>> shutdown) => claudeShutdown = shutdown;
}
