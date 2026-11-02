namespace Kora.Core.Skills;

public sealed class OwnedSkillDeletionUnconfirmedException(Exception innerException)
    : IOException("Owned skill source deletion may have occurred. Do not retry; inspect the exact inventory. No rollback is claimed.", innerException);
