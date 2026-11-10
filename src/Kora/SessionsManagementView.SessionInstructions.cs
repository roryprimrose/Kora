namespace Kora;

internal sealed partial class SessionsManagementView
{
    private void BindSessionInstructionScopes(SessionsViewModel model)
    {
        SessionInstructionScopes.Click += (_, _) => model.OpenInstructionScope();
    }
}
