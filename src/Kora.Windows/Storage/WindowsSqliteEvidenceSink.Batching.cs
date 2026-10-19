using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink
{
    private sealed class PreparedWrite(WindowsSqliteEvidenceSink owner, HostId<EvidenceIdentity> evidenceId,
        string table, Dictionary<string, object> values, string payload, CompletedActivityEnvelope? activity)
        : IPreparedEvidenceWrite
    {
        public WindowsSqliteEvidenceSink Owner => owner;
        public HostId<EvidenceIdentity> EvidenceId => evidenceId;
        public PreparedWrite Retarget(WindowsSqliteEvidenceSink target) =>
            new(target, evidenceId, table, values, payload, activity);
        public void Write() => owner.WriteBatch([this]);

        public void Insert(SqliteConnection connection, SqliteTransaction transaction, long committed, long due)
        {
            WindowsSqliteEvidenceSink.Insert(connection, transaction, table, values, payload, committed, due);
            if (activity is null) { return; }
            for (var ordinal = 0; ordinal < activity.Links.Count; ordinal++)
            {
                var link = activity.Links[ordinal];
                WindowsSqliteEvidenceSink.Insert(connection, transaction, "activity_links",
                    LinkProjection(activity, link, ordinal), Serialize(link), committed, due);
            }

        }
    }

    internal IPreparedEvidenceWrite RetargetPreparedWrite(IPreparedEvidenceWrite write, WindowsSqliteEvidenceSink target) =>
        write is PreparedWrite prepared && ReferenceEquals(prepared.Owner, this)
            ? prepared.Retarget(target)
            : throw new ArgumentException("Only an owned admitted snapshot may be retargeted.", nameof(write));

    public void WriteBatch(IReadOnlyList<IPreparedEvidenceWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count is < 1 or > IBatchedEvidenceSink.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(writes),
                $"An evidence batch must contain between 1 and {IBatchedEvidenceSink.MaximumBatchSize} ordinary writes.");
        }
        var admitted = new PreparedWrite[writes.Count];
        for (var index = 0; index < admitted.Length; index++)
        {
            admitted[index] = writes[index] is PreparedWrite prepared && ReferenceEquals(prepared.Owner, this)
                ? prepared : throw new ArgumentException("Every batch snapshot must have been admitted by this sink.", nameof(writes));
        }
        CommitEvidence(() => CommitTransaction((connection, transaction, committed) =>
        {
            var due = DiagnosticDue(committed);
            foreach (var prepared in admitted) { prepared.Insert(connection, transaction, committed.UtcTicks, due.UtcTicks); }
        }));
    }
}
