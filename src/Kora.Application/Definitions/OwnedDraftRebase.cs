using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class OwnedDraftRebase
{
    internal OwnedDraftRebase(OwnedInstructionSnapshot original, OwnedInstructionSnapshot latest, OwnedDraftComparison draft)
    {
        Original = original;
        Latest = latest;
        Draft = draft;
    }

    public OwnedInstructionSnapshot Original { get; }
    public OwnedInstructionSnapshot Latest { get; }
    public OwnedDraftComparison Draft { get; }
    public bool SameRevision => string.Equals(Original.Revision, Latest.Revision, StringComparison.Ordinal);
    public bool SameSourceBytes => string.Equals(Original.Document.Definition.Digest, Latest.Document.Definition.Digest, StringComparison.Ordinal);
    public bool SameOriginalDraftBytes => string.Equals(Original.Document.Definition.Digest, Draft.RawDigest, StringComparison.Ordinal);
    public string ChangedFields => ArtifactSourceComparison.ChangedFields(Original.Document, Latest.Document);

    public string Summary =>
        $"Explicit source-preserving Rebase; Kora-owned {Draft.Validation.Kind}; exact ID {Draft.Validation.Name}.\n"
        + $"Old: filename {Original.CanonicalFileName}; artifact {Original.Document.Definition.Id}; revision {Original.Revision}; "
        + $"SHA-256 {Original.Document.Definition.Digest}; bytes {Original.Document.ByteCount}; BOM={Original.Document.HasBom}.\n"
        + $"Latest observed: filename {Latest.CanonicalFileName}; artifact {Latest.Document.Definition.Id}; revision {Latest.Revision}; "
        + $"SHA-256 {Latest.Document.Definition.Digest}; bytes {Latest.Document.ByteCount}; BOM={Latest.Document.HasBom}.\n"
        + $"Old/latest: {(SameRevision ? "same exact revision" : "different revision")}; "
        + $"{(SameSourceBytes ? "equal exact source bytes" : "changed exact source bytes")}; fields: {ChangedFields}.\n"
        + $"Exact raw unsaved draft: UTF-8 bytes with latest BOM {Draft.RawByteCount}; SHA-256 {Draft.RawDigest}; "
        + $"raw/latest: {(Draft.ExactBytes ? "equal exact bytes" : "different exact bytes")}; fields: {Draft.ChangedFields}.\n"
        + $"Raw/old (using the disclosed latest BOM): {(SameOriginalDraftBytes ? "equal exact bytes" : "different exact bytes")}.\n"
        + "All three bodies are complete; BOM is disclosed, not inserted into the text. No automatic merge or prepared-Save substitution. "
        + "Confirmation explicitly rereads the source and requires the same observed revision; it adopts only that baseline in memory, "
        + "retains the exact raw draft, and retires prior editor validation/use review. It does not Save or enable use.\n"
        + "Any later Save is a separate complete exact confirmation with optimistic conflict detection. "
        + "Unobserved intervening changes that restore the identical revision are not historical change detection.\n"
        + "Structural observation against latest baseline (not semantic approval):\n" + Draft.Validation.Summary;
}
