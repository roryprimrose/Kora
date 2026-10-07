namespace Kora.Core.Diagnostics;

public enum EvidencePageStatus
{
    Available, Unavailable, ScanLimitReached, MissingOrRemoved,
    Partial, Corrupt, Truncated, Changed, SnapshotExpired, TimedOut,
}
