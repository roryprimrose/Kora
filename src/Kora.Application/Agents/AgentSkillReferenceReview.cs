using Kora.Application.Skills;
using Kora.Core.Agents;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Agents;

/// <summary>Distinct declaration, read-consent, enablement and consumed-guidance stages for one agent review.</summary>
public sealed class AgentSkillReferenceReview : IDisposable
{
    private readonly SharedSkillInvocationWorkflow workflow;
    private readonly BoundedAgentProfile profile;
    private SharedSkillReferenceConsent? declaration;
    private SharedSkillUseReview? review;
    private int retired;

    internal AgentSkillReferenceReview(SharedSkillInvocationWorkflow workflow, BoundedAgentProfile profile)
    { this.workflow = workflow; this.profile = profile; }

    public AgentSkillGuidanceLease? Lease { get; private set; }
    public string DeclarationText => declaration?.DeclarationText ?? string.Empty;
    public string CompleteText => review?.Text ?? string.Empty;
    internal SharedSkillSnapshot Snapshot => workflow.RequireAgentSnapshot(
        review ?? throw new InvalidOperationException("Separately consent to the complete Markdown read first."));
    internal bool HasBundle => review is not null;
    public bool IsEnabled => review?.IsEnabled ?? false;
    internal bool IsCurrent => Volatile.Read(ref retired) == 0
        && (declaration?.IsReadCurrent ?? true) && (review?.IsCurrent ?? true) && (Lease?.IsCurrent ?? true);

    internal async Task DeclareAsync(SharedSkillSource source, SharedSkillSnapshot snapshot,
        Func<bool> current, RequestOrigin origin, CancellationToken token)
    {
        if (declaration is not null || review is not null || !IsCurrent)
        { throw new InvalidOperationException("Begin a fresh agent declaration review."); }
        AgentSkillReferenceResolver.RequireSelection(profile.SharedSkill!, source, snapshot);
        declaration = await workflow.ReviewReferenceDeclarationAsync(source, snapshot, current, origin, token).ConfigureAwait(false);
    }

    internal async Task ConsentAsync(Func<bool> current, RequestOrigin origin, CancellationToken token)
    {
        var consent = declaration ?? throw new InvalidOperationException("Review the selected skill declaration first.");
        review = await workflow.ConsentToReferencesAsync(consent, current, origin, token).ConfigureAwait(false);
        declaration = null;
    }

    internal Task EnableAsync(Func<bool> current, RequestOrigin origin, CancellationToken token) =>
        workflow.EnableAsync(review ?? throw new InvalidOperationException("Read and review the complete skill bundle first."),
            current, origin, token);

    internal async Task AcquireAsync(Func<bool> current, RequestOrigin origin, CancellationToken token)
    {
        if (Lease is not null) { throw new InvalidOperationException("Agent guidance cannot be acquired twice."); }
        Lease = await workflow.AcquireForAgentAsync(review ?? throw new InvalidOperationException("Separately enable the exact skill first."),
            current, origin, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref retired, 1);
        declaration?.Dispose();
        review?.Dispose();
        Lease?.Dispose();
    }
}
