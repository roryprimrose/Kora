namespace Kora.Core.Skills;

public sealed class OwnedSkillPublicationUnconfirmedException(Exception innerException)
    : IOException("A complete skill directory may have been published, but durable readback or audit is unconfirmed. Reopen that exact saved version; do not retry or overwrite.", innerException);
