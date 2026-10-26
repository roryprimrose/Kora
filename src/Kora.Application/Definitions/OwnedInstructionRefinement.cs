using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed class OwnedInstructionRefinement
{
    internal OwnedInstructionRefinement(OwnedInstructionRefinementService owner, OwnedInstructionSnapshot original,
        string draft, string critique, ArtifactFileDocument proposed, RequestOrigin origin, Func<bool> current)
    {
        Owner = owner;
        Original = original;
        Draft = draft;
        Critique = critique;
        Proposed = proposed;
        Origin = origin;
        IsCurrent = current;
    }

    internal OwnedInstructionRefinementService Owner { get; }
    public OwnedInstructionSnapshot Original { get; }
    public string Draft { get; }
    public string Critique { get; }
    public ArtifactFileDocument Proposed { get; }
    public RequestOrigin Origin { get; }
    public Func<bool> IsCurrent { get; }
}
