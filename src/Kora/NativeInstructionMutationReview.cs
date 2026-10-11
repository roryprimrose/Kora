using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class NativeInstructionMutationReview(OwnedInstructionEditorWindow window, string sourcePath, Func<bool> current) : IInstructionMutationReview
{
    public async Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token) =>
        await window.Choose(proposed is null ? "Delete exact owned instruction" : "Save exact owned instruction",
            $"Owned ID: {name}\nExact source: {sourcePath}\nBase revision: {expected?.Revision ?? "(new)"}\n"
            + $"Base digest: {expected?.Document.Definition.Digest ?? "(missing/new)"}\n"
            + $"New digest: {proposed?.Definition.Digest ?? "(delete)"}\n"
            + $"Changed fields: {ChangedFields(expected?.Document, proposed)}\n"
            + "Full original and proposed source shown, including all metadata, body and attribution. "
            + "Final disk comparison is optimistic: an edit after that comparison may be overwritten by complete-byte atomic replacement. "
            + "Only this host-derived owned file changes. Save does not activate guidance.",
            expected?.Document.Text, proposed?.Text, [proposed is null ? "Confirm delete" : "Confirm save", "Cancel"], token) == 0 && current();

    public async Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? observed, ArtifactFileDocument? proposed, CancellationToken token) =>
        (InstructionConflictChoice)await window.Choose("Instruction conflict",
            "Disk changed or the base is missing/replaced. Your draft is preserved.\n"
            + $"Detected disk digest: {observed?.Document.Definition.Digest ?? "(missing)"}\n"
            + "Reload abandons Kora changes; Overwrite explicitly reviews replacement of this detected revision; Cancel keeps the draft. "
            + "A further detected change prompts again. No automatic retry or merge.",
            observed?.Document.Text, proposed?.Text,
            ["Reload from disk (abandon changes)", "Overwrite with Kora changes", "Cancel (keep draft)"], token);

    private static string ChangedFields(ArtifactFileDocument? original, ArtifactFileDocument? proposed)
    {
        if (original is null) { return "new file (all fields and body)"; }
        if (proposed is null) { return "delete entire original"; }
        var changed = original.Metadata.Keys.Union(proposed.Metadata.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(original.Metadata.GetValueOrDefault(key), proposed.Metadata.GetValueOrDefault(key), StringComparison.Ordinal)).ToList();
        if (!string.Equals(original.Definition.Content, proposed.Definition.Content, StringComparison.Ordinal)) { changed.Add("body"); }
        if (!string.Equals(original.Definition.Digest, proposed.Definition.Digest, StringComparison.Ordinal)) { changed.Add("exact source bytes/formatting"); }
        return changed.Count == 0 ? "(unchanged bytes)" : string.Join(", ", changed);
    }
}
