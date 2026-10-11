namespace Kora.Core.Artifacts;

public sealed class InstructionPublicationUnconfirmedException(bool cancellationRequested, Exception cause)
    : IOException("Atomic instruction publication occurred, but completion is unconfirmed. No rollback is claimed.", cause)
{
    public bool CancellationRequested { get; } = cancellationRequested;
}
