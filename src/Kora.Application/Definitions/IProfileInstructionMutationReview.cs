using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

/// <summary>Reviews complete exact profile-original changes independently of source registration.</summary>
public interface IProfileInstructionMutationReview
{
    /// <summary>Confirms replacement of the displayed exact base with the complete proposed bytes.</summary>
    Task<bool> Confirm(CustomizationSnapshot expected, ArtifactFileDocument proposed, CancellationToken token);
    /// <summary>Chooses recovery for a detected content conflict without granting replacement authority.</summary>
    Task<InstructionConflictChoice> Conflict(CustomizationSnapshot observed, ArtifactFileDocument proposed, CancellationToken token);
}
