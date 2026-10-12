namespace Kora.Application.Definitions;

public sealed class InstructionUseRevocationUnconfirmedException(Guid controlReceipt, bool revocationObserved, Exception innerException)
    : IOException("Reviewed use is held or unavailable; revocation completion is unconfirmed. Reload/read back local revision state before a fresh review.", innerException)
{
    public Guid ControlReceipt { get; } = controlReceipt;
    public bool RevocationObserved { get; } = revocationObserved;
}
