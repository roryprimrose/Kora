using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>A finite metadata observation, not a global atomic inventory or body-integrity receipt.</summary>
public sealed record SessionAttachmentPage(
    SessionAttachmentQuery Query, ImmutableArray<SessionAttachmentSummary> Records, string? Next,
    DateTimeOffset ObservedAt, long AuthorityCeiling, string AuthorityDigest, int ScannedRows, bool ScanLimitReached,
    int AuditScannedRows = 0, bool AuthorityValidationPending = false)
{
    public const int MaximumRecords = 16;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumScannedRows = 64;
    public const int MaximumAuditRows = 4096;
    public const int RequestSeconds = 5;
    public const int SnapshotMinutes = 2;
    public const string Scope = "Metadata of retained private Session slots only. No original path or file is read. "
        + "Confirmed is stored copy certification, not body integrity verification. Exact inspection separately validates original bytes, hash and provenance. "
        + "Pages scan at most 64 slots and return at most 16 rows / 64 KiB; excluded or nonmatching slots can yield an empty page with continuation. "
        + "Typed authority is validated in at most 4096-commit segments; pending validation pages contain no attachment rows. "
        + "Continuations expire after two minutes and reject any committed authority change. No global total, whole-inventory atomicity, physical deletion or forensic claim. "
        + "No resume, source consent, model ingestion, export or new authority.";

    public void Validate()
    {
        Query?.Validate();
        if (Query is null || Records.IsDefault || Records.Length > MaximumRecords
            || ScannedRows < Records.Length || ScannedRows > MaximumScannedRows
            || !Enum.IsDefined(Query.Filter)
            || Query.IncludeRemoved && Query.ExactId is null && Query.ExactMediaType is null
                && Query.CopyStatus is null && Query.MinimumStoredBytes is null && Query.MaximumStoredBytes is null
                && Query.Filter == SessionAttachmentFilter.All
                && ScannedRows != Records.Length && Records.Length < MaximumRecords
            || ObservedAt.Offset != TimeSpan.Zero || AuthorityCeiling < 0
            || AuthorityDigest is null || AuthorityDigest.Length != 64
            || AuthorityDigest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || (AuthorityCeiling == 0) != AuthorityDigest.All(c => c == '0')
            || Next is { Length: 0 or > 4096 } || ScanLimitReached && (Next is null || ScannedRows != MaximumScannedRows)
            || AuditScannedRows is < 0 or > MaximumAuditRows
            || AuditScannedRows > AuthorityCeiling
            || AuthorityValidationPending && (Next is null || ScannedRows != 0 || AuditScannedRows != MaximumAuditRows)
            || Next is not null && ScannedRows == 0 && !AuthorityValidationPending
            || Records.Any(row => row is null)
            || Records.Select(row => row.Session).Distinct().Count() != Records.Length)
        {
            throw new InvalidDataException("The attachment page has an invalid bounded observation shape.");
        }
        foreach (var row in Records)
        {
            row.Validate();
            if (row.Profile != Records[0].Profile || row.ObservationSource != Records[0].ObservationSource)
            {
                throw new InvalidDataException("An attachment page cannot mix private profile or storage observation identities.");
            }
            if (!Query.Matches(row))
            {
                throw new InvalidDataException("An attachment row does not match the original metadata query.");
            }
        }
        if (Next is not null) { ValidateCursor(Next); }
        // Fixed-width IDs, revisions, digest/type/date fields and the bounded opaque cursor keep
        // sixteen strictly validated rows below 64 KiB. No arbitrary source text is in this shape.
    }

    public static void ValidateCursor(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 4096) { throw new InvalidDataException("The attachment continuation exceeds its bounded opaque shape."); }
        var parts = text.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length != 64
            || parts[1].Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException("The attachment continuation has an invalid bounded opaque shape.");
        }
        try
        {
            var payload = Convert.FromBase64String(parts[0]);
            if (!string.Equals(parts[0], Convert.ToBase64String(payload), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The attachment continuation is not canonically encoded.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The attachment continuation is not a canonical opaque token.", exception);
        }
    }
}
