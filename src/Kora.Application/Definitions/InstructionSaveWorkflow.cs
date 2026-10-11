using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class InstructionSaveWorkflow(OwnedInstructionEditorActions actions)
{
    public async Task<InstructionSaveOutcome> Save(string name, OwnedInstructionSnapshot? original,
        string draft, bool delete, IInstructionMutationReview review, Func<bool> eligible, CancellationToken token)
    {
        if (!delete) { original?.Document.RequireEditableInstruction(); }
        var proposed = delete ? null : actions.Validate(name, draft, original?.Document.HasBom ?? false);
        if (proposed is not null && (original is null || !string.Equals(original.Document.Text, draft, StringComparison.Ordinal)))
        { proposed = proposed.WithNativeInstructionVersion(name); }
        var expected = original;
        while (true)
        {
            var result = await actions.Save(name, expected, proposed,
                admitted => review.Confirm(name, expected, proposed, admitted), eligible, token).ConfigureAwait(false);
            if (!result.Conflict)
            { return new(result.Published || result.Unchanged, result.Deleted, false,
                result.Published || result.Unchanged ? result.Current : original, result.Unchanged); }
            var choice = await review.Conflict(result.Current, proposed, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!eligible()) { throw new InvalidOperationException("The conflict review lifetime expired."); }
            if (choice == InstructionConflictChoice.Reload)
            { return new(false, false, true, await actions.Inspect(name, eligible, token).ConfigureAwait(false)); }
            if (choice != InstructionConflictChoice.Overwrite)
            { return new(false, false, false, original); }
            expected = result.Current;
        }
    }
}
