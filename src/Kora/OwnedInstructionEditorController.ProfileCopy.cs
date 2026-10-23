using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private CancellationTokenSource? profileForkLifetime;

    private void CancelProfileCopy()
    {
        profileForkLifetime?.Cancel();
        window?.CancelReview();
    }

    private async Task ForkProfileInstruction(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        if (kind != ArtifactKind.Instruction || profileForks is null)
        { throw new InvalidOperationException("Profile instruction copying is unavailable for this kind or runtime."); }
        var destination = opened.CopyNameInput.Text ?? string.Empty;
        OwnedInstructionNames.WriteName(destination);
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        profileForkLifetime = cancellation;
        bool Current() => !cancellation.IsCancellationRequested && eligible()
            && Interlocked.Read(ref sourceRevision) == revision
            && Interlocked.Read(ref copyInputRevision) == inputRevision;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler tick = (_, _) => { if (!Current()) { CancelProfileCopy(); } };
        timer.Tick += tick;
        timer.Start();
        try
        {
            var path = await SelectProfileInstruction(opened, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("The native source selection expired."); }
            if (path is null) { opened.Report("Profile copy selection cancelled. Source unread; editor draft unchanged."); return; }
            var result = await profileForks.Fork(path, destination,
                new NativeProfileInstructionCopyReview(opened, Path.Combine(paths.RoamingRoot, "Instructions"), Current),
                Current, cancellation.Token, reload.ReconcileSavedCopy);
            if (!Current()) { return; }
            opened.Report(result.Published
                ? $"Created disabled Kora instruction {destination}. Original and editor draft unchanged. List, select that new ID and Inspect to review it; no use or grants inherited."
                : "Profile copy review cancelled. No new instruction created; original and editor draft unchanged.");
        }
        finally
        {
            timer.Stop();
            timer.Tick -= tick;
            if (ReferenceEquals(profileForkLifetime, cancellation)) { profileForkLifetime = null; }
            opened.CancelReview();
        }
    }

    private async Task<string?> SelectProfileInstruction(OwnedInstructionEditorWindow opened, CancellationToken token)
    {
        if (profilePicker is not null) { return await profilePicker.SelectAsync(token); }
        if (!opened.StorageProvider.CanOpen) { throw new InvalidOperationException("Trusted native file selection is unavailable."); }
        var selected = await opened.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Select one personal native v1 instruction original (read consent follows)",
            AllowMultiple = false,
            FileTypeFilter = [new("Native instruction original") { Patterns = ["*.instructions.md"] }],
        });
        try
        {
            token.ThrowIfCancellationRequested();
            if (selected.Count == 0) { return null; }
            if (selected.Count != 1) { throw new InvalidOperationException("Select exactly one personal instruction file."); }
            return selected[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("Only a native fixed-drive personal instruction is supported.");
        }
        finally { foreach (var file in selected) { file.Dispose(); } }
    }
}
