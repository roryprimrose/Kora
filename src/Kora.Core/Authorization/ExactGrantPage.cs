using System.Collections.Immutable;

namespace Kora.Core.Authorization;

public sealed record ExactGrantPage(ImmutableArray<ExactGrantInspection> Records, ExactGrantCursor? Next)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumStoredRecordBytes = 16 * 1024;
}
