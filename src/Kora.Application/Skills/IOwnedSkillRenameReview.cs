using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Separates complete source/identity review from consequential native confirmation.</summary>
public interface IOwnedSkillRenameReview
{
    /// <summary>Reviews complete exact old/new packages and their source consequences.</summary>
    Task<bool> ReviewRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token);
    /// <summary>Separately confirms only the previously reviewed immutable rename.</summary>
    Task<bool> ConfirmRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token);
}
