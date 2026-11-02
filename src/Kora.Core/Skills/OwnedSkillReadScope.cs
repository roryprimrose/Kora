namespace Kora.Core.Skills;

/// <summary>Primary inspection never opens reference content; complete reads require separate original-user consent.</summary>
public enum OwnedSkillReadScope
{
    PrimaryDeclaration,
    CompletePackage,
}
