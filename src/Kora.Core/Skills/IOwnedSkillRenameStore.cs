namespace Kora.Core.Skills;

/// <summary>Persists complete owned package identity changes and explicitly resumes retained intent.</summary>
public interface IOwnedSkillRenameStore
{
    /// <summary>Reads bounded retained rename intents, not enablement or execution authority.</summary>
    Task<IReadOnlyList<OwnedSkillRename>> PendingRenames(CancellationToken token);
    /// <summary>Renames the exact reviewed package or resumes its separately reviewed retained intent.</summary>
    Task<OwnedDeclarativeSkillSnapshot> Rename(OwnedSkillRename proposal, bool recovery, Func<bool> eligible,
        Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token);
}
