using Kora.Core.Skills;

namespace Kora.Application.Skills;

public sealed class OwnedSkillGuidanceRefinement
{
    internal OwnedSkillGuidanceRefinement(OwnedSkillGuidanceRefinementService owner, OwnedDeclarativeSkillDocument draft,
        OwnedDeclarativeSkillSnapshot? source, string critique, OwnedDeclarativeSkillDocument proposed,
        OwnedSkillAuthoringAuthority authority, Func<bool> current)
    {
        Owner = owner;
        Draft = draft;
        Source = source;
        Critique = critique;
        Proposed = proposed;
        Authority = authority;
        IsCurrent = current;
    }

    internal OwnedSkillGuidanceRefinementService Owner { get; }
    internal OwnedDeclarativeSkillSnapshot? Source { get; }
    internal OwnedSkillAuthoringAuthority Authority { get; }
    public OwnedDeclarativeSkillDocument Draft { get; }
    public string Critique { get; }
    public OwnedDeclarativeSkillDocument Proposed { get; }
    public Func<bool> IsCurrent { get; }
}
