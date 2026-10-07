namespace Kora.Core.Diagnostics;

public sealed record EvidencePage(EvidencePageStatus Status, IReadOnlyList<EvidenceRecord> Records,
    string? Cursor, IReadOnlyList<EvidenceSource> UnavailableSources, string Disclosure)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 65536;
    public const string StorageDisclosure =
        "Read-only diagnostic projection, not an atomic interaction audit or complete history. "
        + "Bounded startup pruning removes due ordinary diagnostics and spans/links, never audit records. "
        + "Due backlog may remain present. Missing segments may never have been recorded or may be removed. "
        + "Private-profile files are unencrypted; copies are readable. Session/conversation sources are unavailable.";
}
