using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class ReviewedPromptSource
{
    public ReviewedPromptSource(ArtifactDefinition source, BundledPromptSource? bundled = null)
    {
        if (bundled is not null)
        {
            if (!bundled.Matches(source))
            { throw new InvalidDataException("Preparation requires the exact registered immutable bundled prompt."); }
            Description = "Bundled prompt (immutable)";
            SourceText = bundled.SourceText;
        }
        else
        {
            if (source.Kind != ArtifactKind.Prompt || source.Source is not "kora-profile"
                || source.OwnedInstructionName is null || source.OwnedInstructionRevision is null
                || !source.RequiresInstructionReview || source.PromptTemplate is null)
            { throw new InvalidDataException("Preparation requires an exact native v2 owned prompt revision."); }
            Description = "Owned prompt";
            SourceText = source.Content;
        }
    }

    public string Description { get; }
    public string SourceText { get; }
}
