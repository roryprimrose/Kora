using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Complete immutable receipts in sequence order, with explicit bounded-scan omissions.</summary>
public sealed record SessionHistorySearchPage(
    HostId<SessionIdentity> SessionId, HostRevision Generation, bool Disposed, long Snapshot,
    ImmutableArray<SessionHistoryEvent> Records, int Scanned, int Gaps, int OmittedMatches,
    SessionHistorySearchCursor? Next)
{
    public const int MaximumScannedRecords = 200;
    public const string Scope =
        "Passive exact-session lexical OR search over retained committed question/options, final answer/choices "
        + "and typed task/decision metadata only; NFC invariant-case whole words, ordered by host sequence. "
        + "At most 200 scanned receipts, 50 results and 64 KiB per call. Continue even an empty bounded page. "
        + "Gaps include baseline, redacted and unavailable receipts; oversized matching receipts are counted as omitted. "
        + "No bootstrap bodies, reasoning, artifacts, model context, activity renewal, resume or replay.";
}
