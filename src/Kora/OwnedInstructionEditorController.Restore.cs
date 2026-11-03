using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private OwnedInstructionRestorePoint? lastConfirmedSave;
    private CancellationTokenSource? restoreLifetime;

    private void CancelRestoreReview()
    {
        restoreLifetime?.Cancel();
        if (restoreLifetime is not null) { window?.CancelReview(); }
    }

    private void ClearRestore()
    {
        Interlocked.Exchange(ref lastConfirmedSave, null);
        CancelRestoreReview();
        window?.SetRestoreAvailable(false);
    }

    private void RememberConfirmedSave(InstructionSaveOutcome result)
    {
        ClearRestore();
        if (kind != ArtifactKind.Instruction || result.Replaced is not { } previous || result.Current is not { } current
            || !result.Saved || result.Deleted || result.Reloaded || result.Unchanged
            || !actions.IsRefinementSourceObserved(current)
            || !string.Equals(previous.DirectoryIdentity, current.DirectoryIdentity, StringComparison.Ordinal)
            || !string.Equals(previous.Document.Metadata.GetValueOrDefault("kora-format"), "1", StringComparison.Ordinal))
        { return; }
        var point = new OwnedInstructionRestorePoint(previous, current);
        _ = Actions.PrepareRestore(point);
        lastConfirmedSave = point;
        window?.SetRestoreAvailable(true);
    }

    private async Task RestorePrevious(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        if (lastConfirmedSave is not { } point || kind != ArtifactKind.Instruction || profileOriginal is not null
            || uncertain || Actions.IsSourceUnavailable || newDraft || opened.Source.IsReadOnly
            || !ReferenceEquals(baseline, point.Current)
            || !string.Equals(selectedName, point.Current.Name, StringComparison.Ordinal)
            || !string.Equals(opened.NameInput.Text, point.Current.Name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind.Instruction
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, point.Current.Name, StringComparison.Ordinal))
        { throw new InvalidOperationException("No last confirmed save in this editor is available for this exact owned Instruction. No persistent history or profile-original restore is provided."); }
        if (Dirty)
        {
            var choice = await opened.Choose("Unsaved draft before restore",
                "Restore does not silently discard unsaved changes. Save the current draft separately, discard it explicitly, or cancel.",
                originalText, opened.Source.Text, ["Save current draft", "Discard current draft", "Cancel"], token);
            if (!eligible() || !ReferenceEquals(lastConfirmedSave, point)) { return; }
            if (choice == 0)
            {
                await Save(opened, delete: false, eligible, token);
                opened.Report("Draft Save handled separately. Choose Restore previous saved content again to review the last confirmed save now available.");
                return;
            }
            if (choice != 1) { return; }
            opened.Source.Text = originalText;
        }
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref copyInputRevision);
        var input = main.CaptureInstructionRestoreEligibility();
        var clock = restoreTime ?? TimeProvider.System;
        var expiresAt = clock.GetUtcNow().AddMinutes(2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        restoreLifetime = cancellation;
        bool Current() => !cancellation.IsCancellationRequested && eligible() && input()
            && ReferenceEquals(lastConfirmedSave, point) && ReferenceEquals(baseline, point.Current)
            && sourceEpoch == Interlocked.Read(ref sourceRevision) && inputEpoch == Interlocked.Read(ref copyInputRevision)
            && clock.GetUtcNow() < expiresAt;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (!Current()) { CancelRestoreReview(); } };
        timer.Start();
        try
        {
            var result = await Actions.Restore(point, async (proposed, admitted) =>
            {
                if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Preview(proposed, admitted)); }
                return await Preview(proposed, admitted);
            }, new NativeInstructionMutationReview(opened, Path.Combine(paths.RoamingRoot, "Instructions"), Current),
                Current, cancellation.Token);
            if (!Current()) { return; }
            if (!result.Published)
            { opened.Report("Restore cancelled. Current source and draft retained; no restoration or rollback claimed."); return; }
            restoreLifetime = null;
            ClearRestore();
            if (!reload.ReloadNow() || result.Current is not { } restored
                || !actions.IsRefinementSourceObserved(restored))
            {
                uncertain = true;
                throw new InstructionPublicationUnconfirmedException(token.IsCancellationRequested,
                    new IOException("Restored source catalogue reconciliation is unconfirmed."));
            }
            if (!eligible() || !input()) { return; }
            baseline = restored;
            originalText = baseline.Document.Text;
            opened.Source.Text = originalText;
            opened.SetUseReviewAvailable(true);
            opened.Report("Previous complete source saved/read back as a NEW disabled revision and reconciled. No old review, activation, grants or references restored. Last confirmed save in this editor only; no disk history.");
            RememberConfirmedSave(new(true, false, false, baseline) { Replaced = point.Current });
        }
        catch
        {
            ClearRestore();
            throw;
        }
        finally { timer.Stop(); restoreLifetime = null; }

        async Task<bool> Preview(ArtifactFileDocument proposed, CancellationToken admitted)
        {
            if (!Current()) { return false; }
            return await opened.Choose("Restore previous saved content",
                $"Last confirmed save in this editor only; volatile, not persistent history.\nExact current source: {point.Current.CanonicalFileName}; revision: {point.Current.Revision}\n"
                + $"Current: {point.Current.Document.ByteCount} strict UTF-8 bytes; BOM={point.Current.Document.HasBom}; digest={point.Current.Document.Definition.Digest}\n"
                + $"Previous: {proposed.ByteCount} strict UTF-8 bytes; BOM={proposed.HasBom}; digest={proposed.Definition.Digest}\n"
                + "Left: complete CURRENT saved source. Right: exact PREVIOUS saved bytes, including metadata, attribution, body and line endings. "
                + "Continue to a separate normal Save confirmation for a NEW disabled revision. No old use review, activation or grants return. "
                + "Final comparison/publication is optimistic, not content CAS. Detected source changes retire this restore; no conflict overwrite or automatic retry.",
                point.Current.Document.Text, proposed.Text, ["Review separate Save confirmation", "Cancel"], admitted) == 0 && Current();
        }
    }
}