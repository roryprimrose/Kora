using Kora.Application.Definitions;
using Kora.Core.Agents;
using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

/// <summary>Resolves only existing independently enabled owned sources after separate resolution consent.</summary>
public sealed class AgentInstructionReferenceResolver(
    ArtifactCatalogue catalogue, IOwnedInstructionStore store, InstructionRevisionCatalogue revisions)
{
    internal ArtifactCatalogue Catalogue { get; } = catalogue;
    internal InstructionRevisionCatalogue Revisions { get; } = revisions;

    public AgentInstructionReferenceReview Review(BoundedAgentProfile profile, Action revoke)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(revoke);
        if (profile.References.IsEmpty || Catalogue.IsReloadUnavailable)
        { throw new InvalidOperationException("An available referenced profile is required."); }
        var candidates = profile.References.Select(reference =>
        {
            var candidate = Catalogue.Candidates.SingleOrDefault(item => string.Equals(item.Id, reference.Id, StringComparison.Ordinal));
            if (candidate is null || candidate.Kind != ArtifactKind.Instruction || candidate.Source is not "kora-profile"
                || candidate.OwnedInstructionName is null || candidate.OwnedInstructionRevision is null || !candidate.IsUseEnabled)
            { throw new InvalidOperationException("An exact declared owned instruction is missing, disabled or unavailable."); }
            _ = OwnedInstructionRevision.Validate(candidate.OwnedInstructionRevision);
            return candidate;
        }).ToArray();
        return new(this, profile, candidates, revoke);
    }

    internal async Task<OwnedInstructionSnapshot> ReadAsync(ArtifactDefinition candidate, CancellationToken token)
    {
        var snapshot = await store.Read(candidate.OwnedInstructionName!, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The exact owned instruction was withdrawn.");
        snapshot.Document.RequireOwnedCopySource();
        _ = OwnedInstructionRevision.Validate(snapshot.Revision);
        if (!Matches(candidate, snapshot) || !Revisions.IsEnabled(snapshot))
        { throw new InvalidOperationException("The exact enabled instruction source changed; no replacement was selected."); }
        return snapshot;
    }

    internal static bool Matches(ArtifactDefinition candidate, OwnedInstructionSnapshot snapshot) =>
        snapshot.Document.Definition.Kind == ArtifactKind.Instruction
        && snapshot.Document.Definition.Source is "kora-profile"
        && string.Equals(candidate.Id, snapshot.Document.Definition.Id, StringComparison.Ordinal)
        && string.Equals(candidate.OwnedInstructionName, snapshot.Name, StringComparison.Ordinal)
        && string.Equals(candidate.Digest, snapshot.Document.Definition.Digest, StringComparison.Ordinal)
        && string.Equals(candidate.Content, snapshot.Document.Definition.Content, StringComparison.Ordinal)
        && string.Equals(candidate.OwnedInstructionRevision, snapshot.Revision, StringComparison.Ordinal);
}
