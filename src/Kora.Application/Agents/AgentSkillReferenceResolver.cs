using Kora.Application.Skills;
using Kora.Core.Agents;
using Kora.Core.Skills;

namespace Kora.Application.Agents;

/// <summary>Read-only selection from existing registrations; no registration, writer or executor authority.</summary>
public sealed class AgentSkillReferenceResolver(SharedSkillDiscoveryService discovery, SharedSkillInvocationWorkflow workflow)
{
    public Task<IReadOnlyList<SharedSkillSource>> ReadSourcesAsync(Func<bool> eligible, CancellationToken token) =>
        discovery.LoadSourcesAsync(eligible, token);

    public Task<SharedSkillCatalogue> ReadCatalogueAsync(SharedSkillSource source, Func<bool> eligible, CancellationToken token) =>
        discovery.DiscoverAsync(source, eligible, token);

    internal AgentSkillReferenceReview Review(BoundedAgentProfile profile) =>
        profile.SharedSkill is not null ? new(workflow, profile)
            : throw new InvalidOperationException("This profile does not declare shared skill guidance.");

    internal static void RequireSelection(AgentSharedSkillReference reference, SharedSkillSource source,
        SharedSkillSnapshot snapshot)
    {
        if (reference.SourceId != source.Id || snapshot.SourceId != source.Id
            || !string.Equals(snapshot.SourceQualifiedIdentity, reference.Identity, StringComparison.Ordinal) || !snapshot.IsReferenceCandidate
            || snapshot.PackageDirectoryIdentity is null)
        { throw new InvalidOperationException("Explicitly select the exact declared registered v2 skill; no replacement is selected."); }
    }
}
