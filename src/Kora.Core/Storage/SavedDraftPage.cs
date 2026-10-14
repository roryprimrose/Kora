using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Storage;

/// <summary>A bounded mutable observation of explicitly saved, nonempty composer drafts.</summary>
public sealed record SavedDraftPage(SessionListFilter Filter, ImmutableArray<SavedDraftSummary> Records,
    string? Next, int Scanned, int AuditScanned, bool AuthorityPending,
    SessionListSearchKind? QueryKind = null, string? QueryDigest = null,
    SavedDraftMetadataFilter? MetadataFilter = null)
{
    public const int MaximumRecords = 25;
    public const int MaximumScanned = SessionListSearchPage.MaximumScannedRecords;
    public const int MaximumBytes = SessionListSearchPage.MaximumResultBytes;
    public const string Scope = "Explicitly saved composer metadata only, not body integrity verification. "
        + "At most 25 rows / 64 KiB complete projection and 50 candidate slots per click. "
        + "Empty pages can have Next; end is not a global total. Mutable canonical-ID keyset, not an atomic inventory. "
        + "Changes behind the cursor require Refresh; any committed authority change invalidates continuation. "
        + "Name search is ordinal case-sensitive metadata only; exact canonical ID is a separate mode. "
        + "Optional exact positive saved revision and inclusive UTF-8 byte bounds filter metadata only; empty means Any. "
        + "Query edits invalidate observations without reading; Search/Refresh explicitly starts again. "
        + "Opening an exact session never reloads, saves, submits, resumes or renews activity.";

    public static int Size(SavedDraftPage page) => JsonSerializer.SerializeToUtf8Bytes(page).Length;

    public void Validate()
    {
        if (!Enum.IsDefined(Filter) || Records.IsDefault || Records.Length > MaximumRecords
            || Scanned < Records.Length || Scanned > MaximumScanned || AuditScanned is < 0 or > SessionAttachmentPage.MaximumAuditRows
            || AuthorityPending && (Next is null || Scanned != 0)
            || Next is not null && Scanned == 0 && !AuthorityPending)
        { throw new InvalidDataException("Saved draft catalogue has an invalid bounded page."); }
        if (QueryKind is { } kind && !Enum.IsDefined(kind)
            || (QueryKind is null) != (QueryDigest is null)
            || QueryDigest is { } digest && (digest.Length != 64 || digest.Any(character => !char.IsAsciiHexDigitLower(character))))
        { throw new InvalidDataException("Saved draft catalogue has an invalid metadata query scope."); }
        var previous = string.Empty;
        foreach (var row in Records)
        {
            row.Validate();
            var id = row.Session.SessionId.Value.ToString("D");
            if (string.CompareOrdinal(previous, id) >= 0 || Filter != SessionListFilter.All
                && row.Session.IsActive != (Filter == SessionListFilter.Active)
                || row.Source != Records[0].Source || row.Profile != Records[0].Profile
                || row.ControlRevision != Records[0].ControlRevision || row.AuthorityCeiling != Records[0].AuthorityCeiling
                || !string.Equals(row.AuthorityDigest, Records[0].AuthorityDigest, StringComparison.Ordinal)
                || MetadataFilter is not null && !MetadataFilter.Matches(row))
            { throw new InvalidDataException("Saved draft catalogue mixes source scope, filter or canonical ordering."); }
            previous = id;
        }
        if (Next is not null) { SessionAttachmentPage.ValidateCursor(Next); }
        // Fixed-width IDs/digests, validated 120-scalar names and the bounded opaque cursor keep
        // 25 complete records below 64 KiB, including maximal JSON escaping. Readers measure
        // the complete candidate projection before consuming a match; no record is truncated.
    }
}
