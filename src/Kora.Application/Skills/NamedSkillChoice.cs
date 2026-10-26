using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Host-issued immutable binding into the existing canonical source review.</summary>
public sealed class NamedSkillChoice
{
    internal NamedSkillChoice(int number, OwnedDeclarativeSkillSnapshot? owned, SharedSkillSource? source,
        SharedSkillSnapshot? shared, string sourceLabel, bool supported, OwnedSkillAuthoringAuthority original,
        Func<bool> current)
    {
        Number = number;
        Owned = owned;
        Source = source;
        Shared = shared;
        SourceLabel = sourceLabel;
        IsSupported = supported;
        Original = original;
        Current = current;
    }

    public int Number { get; }
    public OwnedDeclarativeSkillSnapshot? Owned { get; }
    public SharedSkillSource? Source { get; }
    public SharedSkillSnapshot? Shared { get; }
    public string SourceLabel { get; }
    public bool IsSupported { get; }
    public OwnedSkillAuthoringAuthority Original { get; }
    public Func<bool> Current { get; }

    public async Task<OwnedSkillUseReview> ReviewOwnedAsync(OwnedSkillInvocationWorkflow workflow, CancellationToken token)
    {
        var expected = Owned ?? throw new InvalidOperationException("This choice is not an owned package.");
        if (!Current()) { throw new InvalidOperationException("The inspected source choice is no longer current."); }
        var review = await workflow.ReviewAsync(expected.Document.Id, expected.Document.Version, Original, Current, token).ConfigureAwait(false);
        if (!expected.IsSameRevision(review.Require(workflow)))
        {
            review.Dispose();
            throw new InvalidDataException("The selected owned source changed after inspection. No latest-name retarget was performed.");
        }
        return review;
    }

    public override string ToString() =>
        $"{Number}. {Owned?.Document.Name ?? Shared!.Name} - {SourceLabel} - version {Owned?.Document.Version ?? Shared!.Version}"
        + (IsSupported ? " - instruction-only review" : " - unavailable; no fallback");
}
