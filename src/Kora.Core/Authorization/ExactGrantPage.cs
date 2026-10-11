using System.Collections.Immutable;

namespace Kora.Core.Authorization;

public sealed record ExactGrantPage(ImmutableArray<ExactGrantInspection> Records, ExactGrantCursor? Next,
    int ScannedRows = 0, int OmittedRecords = 0, Guid? ScannedThrough = null, bool ScanLimitReached = false)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumStoredRecordBytes = 16 * 1024;
    // At most 50 rows are advanced; one additional row is validated as deferred lookahead.
    public const int MaximumScannedRows = MaximumRecords + 1;
}
