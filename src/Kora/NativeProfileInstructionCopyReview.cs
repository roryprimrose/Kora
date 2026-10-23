using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora;

internal sealed class NativeProfileInstructionCopyReview(
    OwnedInstructionEditorWindow window, string destinationRoot, Func<bool> current) : IProfileInstructionCopyReview
{
    public async Task<bool> ConfirmRead(LocalFileMetadata source, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmRead(source, token)); }
        if (!current()) { return false; }
        return await window.Choose("Allow this exact profile instruction read?",
            $"Source: {source.CanonicalPath}\nSize: {source.ByteLength} bytes; maximum 65536 bytes.\n"
            + $"Reader: {ProfileInstructionSnapshot.ReaderVersion}; strict UTF-8, native kora-format: 1 instructions only; header 16 fields / 4096 bytes.\n"
            + $"Allow one complete local capture and at most {ProfileInstructionSnapshot.MaximumFreshnessChecks} bounded exact-byte freshness checks during this one reviewed copy, including at publication. "
            + "Consent is memory-only for this operation, not registration, discovery, model context, editing the original, storage or activation approval. "
            + "No other files, dependencies, folders or network are read. Cancel profile copy withdraws consent and closes private review.",
            "(Source content has not been read.)", "(No destination write is approved.)",
            ["Allow bounded local source read", "Cancel"], token) == 0 && current();
    }

    public async Task<bool> ConfirmCopy(ProfileInstructionCopy copy, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmCopy(copy, token)); }
        if (!current()) { return false; }
        return await window.Choose("Create distinct disabled Kora instruction?",
            $"Original: {copy.Source.Metadata.CanonicalPath}\n"
            + $"Destination: {Path.Combine(destinationRoot, copy.DestinationName + OwnedDefinitionKind.Extension(ArtifactKind.Instruction))}\n"
            + $"New instruction name: {copy.DestinationName}; state: disabled.\n"
            + "Left: entire exact admitted original. Right: entire proposed native instruction with fresh name and explicit source attribution. "
            + "BOM, body, line endings and all other supported metadata are retained. The source must still match exactly; the destination must be absent. "
            + "This approves only a new disabled Kora-owned instruction, not use, grants, execution, model context, egress or reference retargeting. "
            + "The profile original and any open editor draft stay untouched. Inspect the new ID explicitly in the existing editor after success.",
            copy.Source.Document.Text, copy.Proposed.Text, ["Confirm disabled Kora copy", "Cancel"], token) == 0 && current();
    }
}
