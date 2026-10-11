namespace Kora.Application.Definitions;

/// <summary>Explicit local conflict resolution; no automatic overwrite or merge.</summary>
public enum InstructionConflictChoice
{
    /// <summary>Abandon the draft and reload the current original.</summary>
    Reload,
    /// <summary>Review replacing the observed conflict with the exact Kora proposal.</summary>
    Overwrite,
    /// <summary>Keep the original draft without writing.</summary>
    Cancel,
}
