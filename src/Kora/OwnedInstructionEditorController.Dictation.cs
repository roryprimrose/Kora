using System.ComponentModel;

using Kora.Application.Definitions;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController
{
    private OwnedInstructionBodyDictation? bodyDictation;

    private OwnedInstructionBodyDictation.Binding? CaptureBodyDictation(INotifyPropertyChanged source)
    {
        if (disposed || busy || lifetime?.IsCancellationRequested != false || window is not { } opened
            || profileOriginal is not null || kind != ArtifactKind.Instruction || selectedName is not { } name || baseline is null && !newDraft
            || uncertain || actions.IsSourceUnavailable || opened.Source.IsReadOnly || !opened.Source.IsEnabled
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind.Instruction
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Select one supported canonical Kora-owned Instruction draft and a body caret/range. Metadata, prompts and profile originals cannot receive dictation."); }
        var original = baseline;
        OwnedInstructionNames.WriteName(name);
        if (original is not null && (!string.Equals(original.CanonicalFileName, name + ".instructions.md", StringComparison.Ordinal)
            || original.Document.Definition.Kind != ArtifactKind.Instruction))
        { throw new InvalidDataException("Only a canonical owned Instruction original or explicit new owned draft can receive body dictation."); }
        var draft = opened.Source.Text ?? string.Empty;
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, original?.Document.HasBom ?? false),
            ArtifactKind.Instruction, "kora-profile", name);
        document.RequireOwnedCopySource();
        var start = Math.Min(opened.Source.SelectionStart, opened.Source.SelectionEnd);
        var end = Math.Max(opened.Source.SelectionStart, opened.Source.SelectionEnd);
        if (start == end) { start = end = opened.Source.CaretIndex; }
        if (start < draft.Length - document.Body.Length || end > draft.Length)
        { throw new InvalidOperationException("Place the caret or entire selection inside the instruction body, after the metadata delimiter."); }
        var revision = Interlocked.Read(ref sourceRevision);
        var cursorRevision = Interlocked.Read(ref caretRevision);
        var ownership = windowOwnershipRevision;
        var inventoryRevision = Interlocked.Read(ref inventoryEpoch);
        var channel = main.CaptureInstructionBodyDictationEligibility();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var cancellation = lifetime.Token;
        bool SourceCurrent() => !disposed && !busy && !uncertain && profileOriginal is null && !actions.IsSourceUnavailable
            && !cancellation.IsCancellationRequested && ReferenceEquals(window, opened) && ownsHost()
            && ownershipRevision() == ownership && channel() && kind == ArtifactKind.Instruction
            && ReferenceEquals(baseline, original) && string.Equals(selectedName, name, StringComparison.Ordinal)
            && inventoryRevision == Interlocked.Read(ref inventoryEpoch);
        bool Current() => SourceCurrent() && revision == Interlocked.Read(ref sourceRevision)
            && cursorRevision == Interlocked.Read(ref caretRevision);
        string Proposed(string literal)
        {
            if (literal.Length > VoiceInputLimits.MaximumTranscriptCharacters)
            { throw new InvalidDataException("Literal transcription exceeds the existing local voice input bound. Draft unchanged."); }
            var text = draft[..start] + literal + draft[end..];
            var parsed = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, document.HasBom),
                ArtifactKind.Instruction, "kora-profile", name);
            parsed.RequireOwnedCopySource();
            return text;
        }
        async Task<bool> Verify(CancellationToken token)
        {
            var observed = await actions.Inspect(name, Current, token);
            return Current() && (original is null ? observed is null
                : string.Equals(observed?.Revision, original.Revision, StringComparison.Ordinal));
        }
        var target = new ComposerDictationTarget(source, document.Body, request, Current, (literal, admitted) =>
        {
            if (!Current() || !admitted()) { return false; }
            var proposed = Proposed(literal);
            return bodyDictation!.Publish(() =>
            {
                opened.Source.Text = proposed;
                var published = revision + 1;
                var completed = SourceCurrent() && Interlocked.Read(ref sourceRevision) == published && admitted()
                    && string.Equals(opened.Source.Text, proposed, StringComparison.Ordinal);
                if (!completed && main.CanRevealPrivatePresentation && ReferenceEquals(window, opened)
                    && Interlocked.Read(ref sourceRevision) == published
                    && string.Equals(opened.Source.Text, proposed, StringComparison.Ordinal))
                { opened.Source.Text = draft; }
                return completed;
            });
        }, ComposerDictationDestination.OwnedInstructionBody);
        return new(target, request, Current, Verify, Proposed,
            $"Voice context: Instructions / Kora / {name} / body. Frozen UTF-16 range {start}..{end}; "
                + (start == end ? "insert at caret." : "replace selected body text."), cancellation);
    }
}
