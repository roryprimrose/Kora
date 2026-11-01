namespace Kora.Core.Skills;

public sealed record OwnedSkillDeletionSnapshot(OwnedDeclarativeSkillSnapshot Source, string Destination,
    string RootIdentity, string ParentIdentity, string CatalogueIdentity);
