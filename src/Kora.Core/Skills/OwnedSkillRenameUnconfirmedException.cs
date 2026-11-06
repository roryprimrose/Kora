namespace Kora.Core.Skills;

public sealed class OwnedSkillRenameUnconfirmedException(OwnedSkillRenameEffect effect, Exception innerException)
    : IOException("Rename is unconfirmed. List pending renames and separately review Recover rename. "
        + "If no intent remains, inspect both exact old/new identities. Do not retry, overwrite or assume rollback.", innerException)
{
    public OwnedSkillRenameEffect Effect { get; } = effect;
}
