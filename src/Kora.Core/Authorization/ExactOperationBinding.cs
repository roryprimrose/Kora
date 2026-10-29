using Kora.Core.Hosting;
using Kora.Core.Interaction;
using System.Text.Json.Serialization;

namespace Kora.Core.Authorization;

// Digests are host-computed over immutable, canonical snapshots, never accepted from provider claims.
public sealed record ExactOperationBinding
{
    public ExactOperationBinding(string actionId, string sourcePartition, string skillId,
        string definitionDigest, string declaredResourceDigest, string trackedContentDigest,
        string implementationDigest, string invocationDigest, string resourceDigest,
        string identityDigest, string destinationDigest, string transformationDigest, HostRevision policyRevision,
        PermissionGrantTarget? target = null)
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
        Target = target;
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PermissionGrantTarget? Target { get; }

    // Presentation metadata must not retire existing grants or become a new source of execution authority.
    public bool Equals(ExactOperationBinding? other) => other is not null
        && string.Equals(ActionId, other.ActionId, StringComparison.Ordinal)
        && string.Equals(SourcePartition, other.SourcePartition, StringComparison.Ordinal)
        && string.Equals(SkillId, other.SkillId, StringComparison.Ordinal)
        && string.Equals(DefinitionDigest, other.DefinitionDigest, StringComparison.Ordinal)
        && string.Equals(DeclaredResourceDigest, other.DeclaredResourceDigest, StringComparison.Ordinal)
        && string.Equals(TrackedContentDigest, other.TrackedContentDigest, StringComparison.Ordinal)
        && string.Equals(ImplementationDigest, other.ImplementationDigest, StringComparison.Ordinal)
        && string.Equals(InvocationDigest, other.InvocationDigest, StringComparison.Ordinal)
        && string.Equals(ResourceDigest, other.ResourceDigest, StringComparison.Ordinal)
        && string.Equals(IdentityDigest, other.IdentityDigest, StringComparison.Ordinal)
        && string.Equals(DestinationDigest, other.DestinationDigest, StringComparison.Ordinal)
        && string.Equals(TransformationDigest, other.TransformationDigest, StringComparison.Ordinal)
        && PolicyRevision == other.PolicyRevision;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActionId, StringComparer.Ordinal);
        hash.Add(SourcePartition, StringComparer.Ordinal);
        hash.Add(SkillId, StringComparer.Ordinal);
        hash.Add(DefinitionDigest, StringComparer.Ordinal);
        hash.Add(DeclaredResourceDigest, StringComparer.Ordinal);
        hash.Add(TrackedContentDigest, StringComparer.Ordinal);
        hash.Add(ImplementationDigest, StringComparer.Ordinal);
        hash.Add(InvocationDigest, StringComparer.Ordinal);
        hash.Add(ResourceDigest, StringComparer.Ordinal);
        hash.Add(IdentityDigest, StringComparer.Ordinal);
        hash.Add(DestinationDigest, StringComparer.Ordinal);
        hash.Add(TransformationDigest, StringComparer.Ordinal);
        hash.Add(PolicyRevision);
        return hash.ToHashCode();
    }

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
