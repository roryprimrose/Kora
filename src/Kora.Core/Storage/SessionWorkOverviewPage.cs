using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Commands;

namespace Kora.Core.Storage;

/// <summary>A bounded, passive collection of independently observed session work, not a global snapshot.</summary>
public sealed record SessionWorkOverviewPage(
    ImmutableArray<SessionWorkOverviewRow> Records, int Scanned, int Nonmatches,
    bool OutputLimited, SessionWorkOverviewCursor? Next)
{
    public const int MaximumSessions = 10;
    public const int MaximumResultBytes = SessionCommand.MaximumResultBytes;
    public const string Scope = "Passive metadata and independent per-session observations; not globally atomic. "
        + "At most 10 sessions read per click, 50 task/question records per session, 64 KiB complete projection. "
        + "Counts, eligibility, fixed-profile slots and deadlines are as observed, not provider resource contention, ETA, "
        + "reply targets or task authority. Omitted records and unavailable sources are explicit. "
        + "No full-scope totals or all-clear can be inferred from any page. "
        + "Mutable canonical-ID keyset: changes/additions behind the cursor require Refresh; end means no later ID at this read.";

    public static int GetSerializedSize(SessionWorkOverviewPage page) =>
        JsonSerializer.SerializeToUtf8Bytes(page).Length;

    public static byte[] Serialize(SessionWorkOverviewPage page)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(page);
        if (bytes.Length > MaximumResultBytes)
        {
            throw new InvalidDataException("The complete work overview exceeds 64 KiB.");
        }
        return bytes;
    }
}
