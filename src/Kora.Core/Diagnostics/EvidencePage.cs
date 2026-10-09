namespace Kora.Core.Diagnostics;

public sealed record EvidencePage(EvidencePageStatus Status, IReadOnlyList<EvidenceRecord> Records,
    string? Cursor, IReadOnlyList<EvidenceSource> UnavailableSources, string Disclosure,
    DailyEvidenceReport? DailyReport = null)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 65536;
    public const string AuthorityDisclosure =
        "Read-only committed typed interaction-store security audit, schema v3, ordered by commit sequence. "
        + "Separate from diagnostic projections and file mirrors; All and CombinedLog do not include this source. "
        + "The immutable snapshot ceiling excludes later commits. Typed task/question/grant references and digests "
        + "describe the recorded transition, not historical payload reconstruction, a file graph or executable authority. "
        + "Only recorded trace/span IDs are available; no diagnostic span metadata is invented. "
        + "Due audit rows are not pruned by inspection; expiry does not prove physical removal. "
        + "Private-profile files are unencrypted and user-modifiable. Local hash consistency is not forensic "
        + "tamper resistance or an externally anchored checkpoint. Inspection grants no identity, approval or effect authority.";
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
