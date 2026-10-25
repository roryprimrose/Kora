namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Action? OpenSessionInstructionScopes { get; set; }
    public void OpenInstructionScope() => (OpenSessionInstructionScopes
        ?? throw new InvalidOperationException("The native session instruction scope workflow is unavailable."))();
}
