using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class NativeInstructionMutationReview(OwnedInstructionEditorWindow window, string sourceRoot, Func<bool> current,
    ArtifactKind kind = ArtifactKind.Instruction) : IInstructionMutationReview
{
    public async Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmCopy(copy, token)); }
        if (!current()) { return false; }
        var source = copy.Source;
        return await window.Choose($"Save as new owned {kind} copy",
            $"Owned kind: {kind}; source ID: {source.Name}; source identity: {source.Document.Definition.Id}\n"
            + $"Exact source: {Path.Combine(sourceRoot, source.CanonicalFileName)}\nBase revision: {source.Revision}\n"
            + $"Base digest: {source.Document.Definition.Digest}; BOM={source.Document.HasBom}\n"
            + $"Frozen draft digest: {copy.Draft.Definition.Digest}\nDestination ID: {copy.DestinationName}\n"
            + $"Exact new path: {Path.Combine(sourceRoot, copy.DestinationName + OwnedDefinitionKind.Extension(kind))}\n"
            + $"Proposed digest: {copy.Proposed.Definition.Digest}; BOM={copy.Proposed.HasBom}\n"
            + "Left: exact frozen base followed by exact current draft. Right: complete proposed copy, with only the declared name changed from the draft. "
            + "Source and open draft stay untouched. Destination must be absent; no overwrite, source reload, inherited use, grants or reference retargeting. "
            + "The copy remains disabled until separately inspected and reviewed for explicit use.",
            source.Document.Text + "\n\n----- FROZEN CURRENT DRAFT -----\n\n" + copy.Draft.Text,
            copy.Proposed.Text, ["Confirm new copy", "Cancel"], token) == 0 && current();
    }

    public async Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Confirm(name, expected, proposed, token)); }
        if (!current()) { return false; }
        return await window.Choose(proposed is null ? $"Delete exact owned {kind}" : $"Save exact owned {kind}",
            $"Owned kind: {kind}; ID: {name}\nExact source: {Path.Combine(sourceRoot, expected?.CanonicalFileName ?? name + OwnedDefinitionKind.Extension(kind))}\nBase revision: {expected?.Revision ?? "(new)"}\n"
            + $"Base digest: {expected?.Document.Definition.Digest ?? "(missing/new)"}\n"
            + $"New digest: {proposed?.Definition.Digest ?? "(delete)"}\n"
            + $"Changed fields: {ArtifactSourceComparison.ChangedFields(expected?.Document, proposed)}\n"
            + "Full original and proposed source shown, including all metadata, body and attribution. "
            + (expected is null && proposed is not null
                ? "New target must remain absent. Atomic creation never replaces an occupied target; generated instructions have no overwrite fallback. "
                : "Final disk comparison is optimistic: an edit after that comparison may be overwritten by complete-byte atomic replacement. ")
            + "Only this host-derived owned file changes. Save does not permit use or execution.",
            expected?.Document.Text, proposed?.Text, [proposed is null ? "Confirm delete" : "Confirm save", "Cancel"], token) == 0 && current();
    }

    public async Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? observed, ArtifactFileDocument? proposed, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Conflict(observed, proposed, token)); }
        if (!current()) { return InstructionConflictChoice.Cancel; }
        return (InstructionConflictChoice)await window.Choose($"{kind} conflict",
            "Disk changed or the base is missing/replaced. Your draft is preserved.\n"
            + $"Detected disk digest: {observed?.Document.Definition.Digest ?? "(missing)"}\n"
            + "Reload abandons Kora changes; Overwrite explicitly reviews replacement of this detected revision; Cancel keeps the draft. "
            + "A further detected change prompts again. No automatic retry or merge.",
            observed?.Document.Text, proposed?.Text,
            ["Reload from disk (abandon changes)", "Overwrite with Kora changes", "Cancel (keep draft)"], token);
    }

}
