namespace Kora.Core.Artifacts;

/// <summary>An exact observation of private recovery data, not permission to resume publication.</summary>
public sealed record OwnedInstructionRenameRecovery(
    OwnedInstructionRename Proposal, string ObservationRevision, OwnedInstructionRenamePhase Phase);
