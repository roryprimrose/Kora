namespace Kora.Core.Skills;

public interface IOwnedDeclarativeSkillStore
{
    string Destination(OwnedDeclarativeSkillDocument document);
    Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token);
    Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
        OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null);
    Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
        Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
        OwnedDeclarativeSkillSnapshot? expectedSource = null);
}
