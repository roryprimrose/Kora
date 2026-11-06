namespace Kora.Core.Skills;

/// <summary>Exact finite-inventory source deletion owned by the declarative skill store.</summary>
public interface IOwnedSkillDeletionStore
{
    /// <summary>Captures one supported owned source and its native destination and catalogue identities.</summary>
    Task<OwnedSkillDeletionSnapshot> PrepareDeletion(string id, string version, CancellationToken token);
    /// <summary>Revalidates the complete captured source and destination without mutation.</summary>
    Task ValidateDeletion(OwnedSkillDeletionSnapshot expected, CancellationToken token);
    /// <summary>Deletes only the captured inventory through the admitted commit and verifies absence.</summary>
    Task Delete(OwnedSkillDeletionSnapshot expected, Func<bool> eligible,
        Func<Func<bool>, Task<bool>> commit, CancellationToken token);
}
