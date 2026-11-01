namespace Kora.Core.Artifacts;

/// <summary>Intent may be durable; no rollback or completed rename is asserted.</summary>
public sealed class InstructionRenameUnconfirmedException(Exception cause)
    : IOException("Owned instruction rename is recovery-required. Inspect Recover rename before further writes or use; no rollback is claimed.", cause);
