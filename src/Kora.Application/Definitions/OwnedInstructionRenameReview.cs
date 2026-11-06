using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

/// <summary>Frozen identity/source/reference consequences; a separate confirmation is still required.</summary>
public sealed class OwnedInstructionRenameReview
{
    internal OwnedInstructionRenameReview(OwnedInstructionRename proposal, IReadOnlyList<ArtifactDefinition> candidates,
        OwnedInstructionRenameRecovery? recovery)
    {
        Proposal = proposal;
        Candidates = candidates;
        Recovery = recovery;
        Dependents = candidates.Where(candidate => candidate.DeclaredInstructionReferences.Contains(
            proposal.Source.Document.Definition.Id, StringComparer.Ordinal)).ToArray();
        AuditTarget = "definition.owned-rename." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            proposal.Source.Revision + "\n" + proposal.DestinationName + "\n" + proposal.Proposed.Definition.Digest)));
    }

    public OwnedInstructionRename Proposal { get; }
    public OwnedInstructionRenameRecovery? Recovery { get; }
    public IReadOnlyList<ArtifactDefinition> Dependents { get; }
    public string AuditTarget { get; }
    internal IReadOnlyList<ArtifactDefinition> Candidates { get; }
}
