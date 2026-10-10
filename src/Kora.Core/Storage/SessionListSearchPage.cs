using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Commands;

namespace Kora.Core.Storage;

/// <summary>One bounded mutable metadata observation, never historical snapshot or action authority.</summary>
/// <param name="Records">Complete matched metadata records, ordered by canonical session ID.</param>
/// <param name="Scanned">Consumed metadata rows, including nonmatches.</param>
/// <param name="Unnamed">Consumed rows without name metadata; never name matches.</param>
/// <param name="OutputLimited">More rows remain because the requested result or complete byte bound was reached.</param>
/// <param name="Next">Continue after the last consumed row even when this result has no matches.</param>
public sealed record SessionListSearchPage(ImmutableArray<SessionWorkspaceEntry> Records, int Scanned,
    int Unnamed, bool OutputLimited, SessionListSearchCursor? Next)
{
    public const int MaximumScannedRecords = SessionPage<SessionWorkspaceEntry>.MaximumRecords;
    public const int MaximumResultBytes = SessionCommand.MaximumResultBytes;
    public const string Scope = "Passive local session metadata only. Canonical-ID order; at most 50 rows scanned per click and 64 KiB complete output. "
        + "Unmatched rows are not results; no matching record is truncated or silently omitted. "
        + "Mutable keyset, not an atomic snapshot: renames/state/deletion and additions behind the cursor require a fresh Search. "
        + "End means no later row at this observation, not a complete historical inventory. Names never grant authority.";

    public static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > SessionPage<SessionWorkspaceEntry>.MaximumRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The list result limit must be 1-50.");
        }
    }

    public static int GetSerializedSize(SessionListSearchPage page) =>
        JsonSerializer.SerializeToUtf8Bytes(page, Json).Length;

    public static byte[] Serialize(SessionListSearchPage page)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(page, Json);
        if (bytes.Length > MaximumResultBytes)
        {
            throw new InvalidDataException("Session list search exceeds the complete 64 KiB output bound.");
        }
        return bytes;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
