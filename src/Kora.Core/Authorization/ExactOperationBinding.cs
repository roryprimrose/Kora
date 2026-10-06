using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Authorization;

// Digests are host-computed over immutable, canonical snapshots, never accepted from provider claims.
public sealed record ExactOperationBinding
{
    public ExactOperationBinding(string actionId, string sourcePartition, string skillId,
        string definitionDigest, string declaredResourceDigest, string trackedContentDigest,
        string implementationDigest, string invocationDigest, string resourceDigest,
        string identityDigest, string destinationDigest, string transformationDigest, HostRevision policyRevision)
    {
        ActionId = InteractionValidation.Identifier(actionId);
        SourcePartition = InteractionValidation.Identifier(sourcePartition);
        SkillId = InteractionValidation.Identifier(skillId);
        DefinitionDigest = InteractionValidation.Digest(definitionDigest);
        DeclaredResourceDigest = InteractionValidation.Digest(declaredResourceDigest);
        TrackedContentDigest = InteractionValidation.Digest(trackedContentDigest);
        ImplementationDigest = InteractionValidation.Digest(implementationDigest);
        InvocationDigest = InteractionValidation.Digest(invocationDigest);
        ResourceDigest = InteractionValidation.Digest(resourceDigest);
        IdentityDigest = InteractionValidation.Digest(identityDigest);
        DestinationDigest = InteractionValidation.Digest(destinationDigest);
        TransformationDigest = InteractionValidation.Digest(transformationDigest);
        if (policyRevision.Value <= 0)
        {
            throw new InvalidDataException("An operation policy revision is required.");
        }
        PolicyRevision = policyRevision;
    }

    public string ActionId { get; }
    public string SourcePartition { get; }
    public string SkillId { get; }
    public string DefinitionDigest { get; }
    public string DeclaredResourceDigest { get; }
    public string TrackedContentDigest { get; }
    public string ImplementationDigest { get; }
    public string InvocationDigest { get; }
    public string ResourceDigest { get; }
    public string IdentityDigest { get; }
    public string DestinationDigest { get; }
    public string TransformationDigest { get; }
    public HostRevision PolicyRevision { get; }

    public bool IsSameDefinition(ExactOperationBinding other) =>
        string.Equals(ActionId, other.ActionId, StringComparison.Ordinal)
        && string.Equals(SourcePartition, other.SourcePartition, StringComparison.Ordinal)
        && string.Equals(SkillId, other.SkillId, StringComparison.Ordinal);

    public bool HasObservedContentChange(ExactOperationBinding other) =>
        IsSameDefinition(other)
        && (!string.Equals(DefinitionDigest, other.DefinitionDigest, StringComparison.Ordinal)
            || !string.Equals(DeclaredResourceDigest, other.DeclaredResourceDigest, StringComparison.Ordinal)
            || !string.Equals(TrackedContentDigest, other.TrackedContentDigest, StringComparison.Ordinal)
            || !string.Equals(ImplementationDigest, other.ImplementationDigest, StringComparison.Ordinal));
}
