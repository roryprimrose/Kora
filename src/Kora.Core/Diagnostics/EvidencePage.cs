namespace Kora.Core.Diagnostics;

public sealed record EvidencePage(EvidencePageStatus Status, IReadOnlyList<EvidenceRecord> Records,
    string? Cursor, IReadOnlyList<EvidenceSource> UnavailableSources, string Disclosure)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 65536;
    public const string StorageDisclosure =
        "Read-only diagnostic projection, not an atomic interaction audit or complete history. "
        + "Due records remain physically present; pruning is not implemented. Missing segments may never have been recorded or may be removed. "
        + "Private-profile files are unencrypted; copies are readable. Session/conversation sources are unavailable.";
}
