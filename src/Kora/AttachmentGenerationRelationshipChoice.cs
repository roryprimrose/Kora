using Kora.Core.Storage;

namespace Kora;

internal sealed record AttachmentGenerationRelationshipChoice(AttachmentGenerationRelationship Relationship)
{
    public override string ToString() => Relationship switch
    {
        AttachmentGenerationRelationship.Any => "Any",
        AttachmentGenerationRelationship.SameObservedGeneration => "Same observed generation",
        AttachmentGenerationRelationship.EarlierObservedGeneration => "Earlier observed generation",
        _ => "Invalid observed generation relationship"
    };
}
