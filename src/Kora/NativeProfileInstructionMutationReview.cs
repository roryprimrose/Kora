using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora;

internal sealed class NativeProfileInstructionMutationReview(OwnedInstructionEditorWindow window, Func<bool> current)
    : IProfileInstructionMutationReview
{
    public async Task<bool> Confirm(CustomizationSnapshot expected, ArtifactFileDocument proposed, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Confirm(expected, proposed, token)); }
        if (!current()) { return false; }
        var before = expected.Files.Single();
        var original = ArtifactFileDocument.Parse(before.CopyBytes(), ArtifactKind.Instruction, "profile-instruction", expected.Source.Name);
        return await window.Choose("Save exact registered profile instruction original",
            $"Shared original: {expected.Source.Location}\n"
            + $"Changed fields: {ArtifactSourceComparison.ChangedFields(original, proposed)}\n"
            + $"Original UTF-8 BOM: {before.HasBom}; proposed UTF-8 BOM: {proposed.HasBom}\n"
            + "Left: complete exact original. Right: complete exact replacement. All source, metadata and line endings are shown without normalization. "
            + "Only this existing host-chosen file is replaced; there is no new destination or permission. Other applications and external sync share this original. "
            + "Final identity/digest comparison is optimistic, not content CAS: an edit after comparison may be overwritten. "
            + "Save remains separate from registration and use. Changed content is disabled; validation is not semantic harmlessness or activation.",
            before.Text, proposed.Text, ["Confirm save original", "Cancel"], token) == 0 && current();
    }

    public async Task<InstructionConflictChoice> Conflict(CustomizationSnapshot observed, ArtifactFileDocument proposed, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Conflict(observed, proposed, token)); }
        if (!current()) { return InstructionConflictChoice.Cancel; }
        return (InstructionConflictChoice)await window.Choose("Profile instruction original conflict",
            $"Shared original: {observed.Source.Location}\n"
            + "Detected external content changed. Your draft is retained. Reload abandons it; Overwrite requires another complete exact Save review; Cancel keeps it. "
            + "A further detected change prompts again. Missing, inaccessible, replaced, aliased or redirected sources cannot be overridden.",
            observed.Files.Single().Text, proposed.Text,
            ["Reload from disk (abandon changes)", "Overwrite with Kora changes", "Cancel (keep draft)"], token);
    }
}
