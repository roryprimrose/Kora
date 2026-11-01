using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed class OwnedInstructionRefinement
{
    internal OwnedInstructionRefinement(OwnedInstructionRefinementService owner, string name, OwnedInstructionSnapshot? original,
        string draft, string critique, ArtifactFileDocument proposed, RequestOrigin origin, Func<bool> current)
    {
        Owner = owner;
        Name = name;
        Original = original;
        Draft = draft;
        Critique = critique;
        Proposed = proposed;
        Origin = origin;
        IsCurrent = current;
    }

    internal OwnedInstructionRefinementService Owner { get; }
    public string Name { get; }
    public OwnedInstructionSnapshot? Original { get; }
    public string Draft { get; }
    public string Critique { get; }
    public ArtifactFileDocument Proposed { get; }
    public RequestOrigin Origin { get; }
    public Func<bool> IsCurrent { get; }
}
