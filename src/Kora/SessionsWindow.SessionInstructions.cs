namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindSessionInstructionScopes(SessionsViewModel model)
    {
        SessionInstructionScopes.Click += (_, _) => model.OpenInstructionScope();
    }
}
