namespace Kora.Core.Diagnostics;

public sealed record EvidencePage(EvidencePageStatus Status, IReadOnlyList<EvidenceRecord> Records,
    string? Cursor, IReadOnlyList<EvidenceSource> UnavailableSources, string Disclosure,
    DailyEvidenceReport? DailyReport = null)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 65536;
    public const string StorageDisclosure =
        "Read-only diagnostic projection, not an atomic interaction audit or complete history. "
        + "Bounded startup pruning removes due ordinary diagnostics and spans/links, never audit records. "
        + "Due backlog may remain present. Missing segments may never have been recorded or may be removed. "
        + "Private-profile files are unencrypted; copies are readable. Session/conversation sources are unavailable. "
        + "All selects SQLite only; DailyLog is an independent bounded file snapshot. "
        + "Opt-in CombinedLog pairs independent SQLite and file snapshots, not an atomic combined ledger: "
        + "SQLite ordinary logs in commit-time/ID order, then daily records in file/offset order, without deduplication or causal ranking. "
        + "DailyLog is user-modifiable, has no authoritative audit, database commit time, activity graph or record due dates; "
        + "unsupported copies and gaps are reported. Its correlation is never execution identity or permission.";
}
