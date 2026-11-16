namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<bool> copilotSetupQuiescent = static () => true;
    private Func<bool> ollamaEvaluationQuiescent = static () => true;
    public event EventHandler? CopilotAccountSetupRequested;
    public void ConfigureCopilotAccount() => CopilotAccountSetupRequested?.Invoke(this, EventArgs.Empty);
    public void BindCopilotSetupQuiescence(Func<bool> observe) => copilotSetupQuiescent = observe;
    public void BindOllamaEvaluationQuiescence(Func<bool> observe) => ollamaEvaluationQuiescent = observe;
}
