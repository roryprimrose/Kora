using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Authorization;

/// <summary>A finite passive observation, never exhaustive execution evidence or continuing authority.</summary>
public sealed record ExactGrantHistoryPage(
    ExactGrantInspection? Current, ImmutableArray<ExactGrantHistoryEntry> Records, string? Next,
    DateTimeOffset ObservedAt, long AuditCeiling, string AuditCeilingDigest,
    long ScannedThrough, int ScannedRows, bool ScanLimitReached, int NonmatchingRows = 0)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumScannedRows = 4096;
    public const int RequestSeconds = 5;
    public const int SnapshotMinutes = 15;
    public const string Disclosure = "Committed security_audit_events only, with exact typed grant membership. "
        + "Requested review is not a committed grant change. A committed consume is not proof an action executed. "
        + "Creation time, last use and count are stored metadata, not missing use rows or exhaustive retained history. "
        + "Unlinked review/failed attempts and diagnostic mirrors are unavailable in this exact-ID history. "
        + "Expired-but-present is a retained deadline, not physical removal. Gaps/retention and execution/applicability remain unknown. "
        + "Observed finite snapshot; not forensic or tamperproof evidence. No replay, export or new authority. "
        + "Audit correlation and approval IDs are committed correlation metadata only, not identity or execution authority. "
        + "Request session/request/task IDs match only the committed envelope, not the current viewer or original grant proposal; no session selection. "
        + "Absent approval means validated metadata without an ID, not unknown. "
        + "Collateral counts describe other changes/requested grant references on the validated committed envelope only; "
        + "they disclose no collateral identities/details and confer no execution, completeness, approval, grant, revoke or replay authority.";

    public string Limitations => Disclosure;

    public static byte[] Serialize(ExactGrantHistoryPage page) => JsonSerializer.SerializeToUtf8Bytes(page);
}
