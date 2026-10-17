using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class OwnedDraftComparison
{
    internal OwnedDraftComparison(OwnedInstructionSnapshot? original, string draft, OwnedDraftValidation validation)
    {
        var bom = original?.Document.HasBom ?? false;
        // Reject before encoding or rendering; never shorten an over-budget memory buffer.
        if (draft.Length > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The raw draft exceeds the complete 65536-byte comparison bound."); }
        var bytes = ArtifactFileDocument.Encode(draft, bom);
        if (bytes.Length > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The raw draft exceeds the complete 65536-byte comparison bound including BOM."); }
        Original = original;
        Draft = draft;
        Validation = validation;
        RawByteCount = bytes.Length;
        RawDigest = ArtifactSourceComparison.Digest(bytes);
        ArtifactFileDocument? raw = null;
        try { raw = ArtifactFileDocument.Parse(bytes, validation.Kind, "kora-profile", validation.Name); }
        catch (InvalidDataException)
        {
            // Invalid structure remains visible verbatim, but cannot supply parsed field claims.
        }
        ChangedFields = raw is null ? "Raw metadata/body comparison unavailable: invalid structure."
            : ArtifactSourceComparison.ChangedFields(original?.Document, raw);
    }

    public OwnedInstructionSnapshot? Original { get; }
    public string Draft { get; }
    public OwnedDraftValidation Validation { get; }
    public int RawByteCount { get; }
    public string RawDigest { get; }
    public string ChangedFields { get; }
    public bool ExactBytes => Original is not null && string.Equals(Original.Document.Definition.Digest, RawDigest, StringComparison.Ordinal);

    public string Summary =>
        $"Passive historical comparison; owned {Validation.Kind}; ID {Validation.Name}; source: Kora-owned.\n"
        + $"Left: frozen inspected baseline; revision {Original?.Revision ?? "(missing/new baseline)"}; "
        + $"bytes {Original?.Document.ByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(missing)"}; SHA-256 {Original?.Document.Definition.Digest ?? "(missing)"}; BOM={Original?.Document.HasBom ?? false}.\n"
        + $"Right: exact raw memory draft; UTF-8 bytes including inherited BOM {RawByteCount}; SHA-256 {RawDigest}; BOM={Validation.HasBom}; "
        + $"raw versus baseline: {(ExactBytes ? "exact bytes" : "changed or new")}; changed fields: {ChangedFields}\n"
        + "Source panes preserve all metadata, attribution and newlines; BOM is disclosed above, not inserted into text. "
        + "Raw draft is NOT canonical Save bytes. Separately labelled prepared-Save structural observation:\n"
        + Validation.Summary + "\n"
        + "No disk read/reload, mutation, activation, consent, approval or review-use token. No semantic safety, current disk integrity or diff patch claim. Pane-local literal Find and Close only.";
}
