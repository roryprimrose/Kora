namespace Kora.Core.Artifacts;

/// <summary>Bounded native identity publication and explicit recovery for one owned instruction root.</summary>
public interface IOwnedInstructionRenameStore
{
    /// <summary>Publishes the exact rename without replacing a destination; uncertain effects require recovery.</summary>
    Task<OwnedInstructionSnapshot> Rename(OwnedInstructionRename proposal, Func<bool> eligible,
        Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token);

    /// <summary>Observes an unfinished rename without resuming or rolling back its effects.</summary>
    Task<OwnedInstructionRenameRecovery?> ReadRenameRecovery(CancellationToken token);

    /// <summary>Resumes only the separately reviewed exact unfinished rename and observed filesystem state.</summary>
    Task<OwnedInstructionSnapshot> RecoverRename(OwnedInstructionRenameRecovery recovery, Func<bool> eligible,
        Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token);
}
