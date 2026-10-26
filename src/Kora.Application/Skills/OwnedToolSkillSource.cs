namespace Kora.Application.Skills;

internal sealed class OwnedToolSkillSource(OwnedSkillInvocationWorkflow workflow, OwnedSkillUseReview review,
    OwnedSkillAuthoringAuthority authority) : ReviewedToolSkillSource
{
    internal OwnedSkillInvocationWorkflow Workflow { get; } = workflow;
    internal OwnedSkillUseReview Review { get; } = review;
    internal OwnedSkillAuthoringAuthority Authority { get; } = authority;
    internal override string Text => Review.Text;
    internal override string AuditIdentity => "definition.owned-skill";
    internal override bool IsCurrent => Review.IsCurrent;
    public override void Dispose() => Review.Dispose();
}
