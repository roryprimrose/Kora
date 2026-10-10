using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionHistoryPage(
    HostId<SessionIdentity> SessionId, HostRevision Generation, bool Disposed,
    long Snapshot, ImmutableArray<SessionHistoryEvent> Records, SessionHistoryCursor? Next)
{
    public const int MaximumRecords = 50;
    public const int MaximumBytes = 65536;
    public const string Scope =
        "Host-committed questions/final answers, decision metadata and task-state receipts only. "
        + "Task success is not proof of an external effect. Pre-history order/content is unavailable. "
        + "Bootstrap user/model messages and response bodies, captions, file previews and shared skill text are not recorded. "
        + "Passive exact-session lexical search is separate. No composer, model context, Ask Evidence, export, queue, automatic resume or replay.";

    public static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > MaximumRecords) { throw new ArgumentOutOfRangeException(nameof(limit)); }
    }
}
