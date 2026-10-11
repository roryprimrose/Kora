using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

/// <summary>Native exact-byte mutation review and explicit conflict choices.</summary>
public interface IInstructionMutationReview
{
    /// <summary>Confirms the exact original and proposed bytes, including destructive deletion.</summary>
    Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token);
    /// <summary>Resolves a detected conflicting base without abandoning or overwriting implicitly.</summary>
    Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? current, ArtifactFileDocument? proposed, CancellationToken token);
    /// <summary>Reviews the frozen owned source, current draft and full same-kind new-file proposal.</summary>
    Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token);
}
