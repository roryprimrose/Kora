namespace Kora.Windows.Dependencies;

internal sealed class CopilotEgressDeniedException(Exception? innerException = null)
    : IOException("The exact Copilot transmission was not admitted.", innerException);
