using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Complete source review and a separate explicit deletion confirmation.</summary>
public interface IOwnedSkillDeletionReview
{
    /// <summary>Displays exact source, inventory, declared dependencies, use impact and limitations.</summary>
    Task<bool> ReviewDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token);
    /// <summary>Separately confirms only that exact reviewed deletion.</summary>
    Task<bool> ConfirmDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token);
}
