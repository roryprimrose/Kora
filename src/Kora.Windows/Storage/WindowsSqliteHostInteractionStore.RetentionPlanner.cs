using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    internal string RetentionPlannerStamp(CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        _ = RequireInspectionIdentity();
        using var connection = database.OpenReadOnly(token);
        return RetentionPlannerStamp(connection);
    }

    private string RetentionPlannerStamp(SqliteConnection connection)
    {
        var identity = RequireInspectionIdentity();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.sequence,substr(h.hash,1,65),(SELECT sequence FROM security_audit_events ORDER BY sequence DESC LIMIT 1),
                (SELECT substr(hash,1,65) FROM security_audit_events ORDER BY sequence DESC LIMIT 1)
            FROM authority_head h WHERE singleton=1;
            """;
        using var row = command.ExecuteReader();
        if (!row.Read()) { throw new InvalidDataException("The retention authority head is missing."); }
        var sequence = row.GetInt64(0);
        var hash = row.GetString(1);
        if (sequence < 0 || hash.Length != 64
            || sequence == 0 && (!row.IsDBNull(2) || !Same(hash, EmptyHash))
            || sequence > 0 && (!IsHex(hash, 64) || row.IsDBNull(2) || row.GetInt64(2) != sequence || !Same(hash, row.GetString(3)))
            || row.Read())
        {
            throw new InvalidDataException("The retention authority head disagrees with its committed suffix.");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(':', identity, database.ReadPreviewStamp(), sequence.ToString(CultureInfo.InvariantCulture), hash))));
    }

    internal SessionRetentionPlanResult ReadRetentionPlan(SessionRetentionSettings proposed, DateTimeOffset cutoff,
        CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        var stamp = RetentionPlannerStamp(connection);
        var planner = new SessionRetentionPlanner(proposed, cutoff);
        var started = time.GetTimestamp();
        bool Expired() => time.GetElapsedTime(started) >= SessionRetentionPlanner.ScanTimeout;
        void RequireTime()
        {
            token.ThrowIfCancellationRequested();
            if (Expired()) { throw new TimeoutException("The bounded session-retention metadata observation timed out."); }
        }
        var authority = new Dictionary<(string Kind, string Id), (long Sequence, AuthorityChange Change)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT sequence,substr(correlation_id,1,37),substr(previous_hash,1,65),substr(hash,1,65),
                    length(CAST(envelope AS BLOB)),
                    CASE WHEN length(CAST(envelope AS BLOB)) BETWEEN 1 AND 131072
                        THEN json_extract(envelope,'$.Changes') END
                FROM security_audit_events ORDER BY sequence LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            long sequence = 0;
            var previous = EmptyHash;
            while (rows.Read())
            {
                RequireTime();
                _ = ParseId(rows.GetString(1));
                var hash = rows.GetString(3);
                var encodedBytes = rows.GetInt64(4);
                if (rows.GetInt64(0) != ++sequence || !Same(previous, rows.GetString(2))
                    || !IsHex(hash, 64)
                    || encodedBytes is < 1 or > 131072 || rows.IsDBNull(5))
                {
                    throw new InvalidDataException("The retention authority metadata prefix is inconsistent.");
                }
                var changes = HostInteractionCodec.Decode<AuthorityChange[]>(rows.GetString(5));
                foreach (var change in changes)
                {
                    RequireTime();
                    if (!IsValidAuthorityChange(change))
                    {
                        throw new InvalidDataException("The committed authority change metadata is invalid.");
                    }
                    _ = ParseId(change.Id);
                    if (change.Kind is "session" or "retention" or "question")
                    {
                        authority[(change.Kind, change.Id)] = (sequence, change);
                    }
                }
                if (!planner.AdmitCandidate(checked((int)encodedBytes))) { return planner.Finish(stamp); }
                previous = hash;
            }
            using var head = connection.CreateCommand();
            head.CommandText = "SELECT sequence FROM authority_head WHERE singleton=1;";
            if (head.ExecuteScalar() is not long ceiling || ceiling != sequence)
            {
                throw new InvalidDataException("The retention authority prefix has a missing suffix.");
            }
        }
        var unknown = new HashSet<HostId<SessionIdentity>>();
        var workHeld = new HashSet<HostId<SessionIdentity>>();
        var workSessions = new HashSet<HostId<SessionIdentity>>();
        var taskIds = new HashSet<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT substr(task_id,1,37) AS task_id,substr(request_id,1,37) AS request_id,
                    substr(session_id,1,37) AS session_id,origin,substr(invocation_id,1,37) AS invocation_id,revision,state
                FROM host_tasks ORDER BY task_id LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                RequireTime();
                var task = WindowsSqliteHostTaskStore.Decode(rows);
                _ = WindowsSqliteHostTaskStore.ValidateTaskLedger(connection, task);
                if (!planner.AdmitCandidate(192)) { return planner.Finish(stamp); }
                var id = task.Request.SessionId;
                taskIds.Add(task.Request.TaskId.Value);
                workSessions.Add(id);
                if (task.State is HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded or HostTaskState.Unknown)
                {
                    workHeld.Add(id);
                }
                else if (task.State == HostTaskState.Unavailable) { unknown.Add(id); }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT substr(task_id,1,37),revision,state FROM host_task_events ORDER BY task_id,revision LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                RequireTime();
                var id = ParseId(rows.GetString(0));
                if (!taskIds.Contains(id) || rows.GetInt64(1) is < 1 or > 3
                    || rows.GetInt64(2) is < 0 or > (long)HostTaskState.Unavailable)
                {
                    throw new InvalidDataException("Orphan or invalid committed work-state metadata holds the observation.");
                }
                if (!planner.AdmitCandidate(40)) { return planner.Finish(stamp); }
            }
        }
        // Question status is embedded in a user-content payload. Even an answered-looking question
        // cannot prove absence of a hold without that read; conservatively exclude it as unknown.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT substr(question_id,1,37),substr(session_id,1,37),revision,audit_sequence
                FROM host_questions ORDER BY question_id LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                RequireTime();
                var question = ParseId(rows.GetString(0));
                var id = new HostId<SessionIdentity>(ParseId(rows.GetString(1)));
                var revision = rows.GetInt64(2);
                if (revision <= 0 || !authority.TryGetValue(("question", question.ToString("D")), out var latest)
                    || latest.Sequence != rows.GetInt64(3) || latest.Change.Revision != revision)
                {
                    throw new InvalidDataException("The question hold has no exact committed metadata authority.");
                }
                if (!planner.AdmitCandidate(96)) { return planner.Finish(stamp); }
                unknown.Add(id);
                workSessions.Add(id);
            }
        }

        var sessions = new HashSet<HostId<SessionIdentity>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT substr(s.session_id,1,37),s.generation,s.state,s.audit_sequence,
                    substr(r.last_activity,1,34),r.archive_days,r.delete_days,r.perpetual,r.purged,r.exemption_audit,
                    length(CAST(r.last_activity AS BLOB))
                FROM work_sessions s LEFT JOIN session_retention r ON r.session_id=s.session_id
                ORDER BY s.session_id LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                RequireTime();
                var id = new HostId<SessionIdentity>(ParseId(rows.GetString(0)));
                var generation = new HostRevision(rows.GetInt64(1));
                var lifecycle = checked((int)rows.GetInt64(2));
                if (lifecycle is < 0 or > 2 || rows.IsDBNull(4) || rows.GetInt64(10) != 33)
                {
                    throw new InvalidDataException("A session has missing or malformed retention metadata.");
                }
                var text = rows.GetString(4);
                var last = DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);
                if (!Same(text, Timestamp(last))) { throw new InvalidDataException("The activity clock is not canonical UTC."); }
                var settings = new SessionRetentionSettings(checked((int)rows.GetInt64(5)), checked((int)rows.GetInt64(6)));
                var kept = rows.GetInt64(7);
                var purged = rows.GetInt64(8);
                if (kept is not (0 or 1) || purged is not (0 or 1))
                {
                    throw new InvalidDataException("Retention flags are invalid.");
                }
                ValidatePlannerAuthority(authority, rows.GetInt64(3),
                    SessionChange(new(id, generation, lifecycle == 0), lifecycle));
                if (rows.IsDBNull(9))
                {
                    if (kept == 1 || authority.ContainsKey(("retention", Id(id))))
                    {
                        throw new InvalidDataException("The latest retention exemption receipt is missing.");
                    }
                }
                else
                {
                    ValidatePlannerAuthority(authority, rows.GetInt64(9), ExemptionChange(id, kept == 1));
                }
                var state = new SessionRetentionState(id, last, last.AddDays(settings.ArchiveDays),
                    last.AddDays(settings.DeleteDays), kept == 1, purged == 1);
                // Fixed metadata charges cover the maximum canonical encoded width, not session bodies.
                if (!planner.Add(state, lifecycle, liveControlSessions.Contains(id) || workHeld.Contains(id), unknown.Contains(id), 192))
                {
                    return planner.Finish(stamp);
                }
                sessions.Add(id);
            }
        }
        // A separate bounded key-only pass detects orphan clocks without an unbounded join/count.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT substr(session_id,1,37) FROM session_retention ORDER BY session_id LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates + 1);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                RequireTime();
                var id = new HostId<SessionIdentity>(ParseId(rows.GetString(0)));
                if (!sessions.Contains(id)) { throw new InvalidDataException("A retention clock has no session authority."); }
                if (!planner.AdmitCandidate(36)) { return planner.Finish(stamp); }
            }
        }
        if (workSessions.Any(id => !sessions.Contains(id)))
        {
            throw new InvalidDataException("Work-hold metadata has no session authority.");
        }
        foreach (var key in authority.Keys)
        {
            RequireTime();
            if (key.Kind == "session" && !sessions.Contains(new(ParseId(key.Id))))
            {
                throw new InvalidDataException("A committed session authority has no retained session metadata.");
            }
        }
        RequireTime();
        if (!Same(stamp, RetentionPlannerStamp(connection)))
        {
            throw new InvalidDataException("Private retention metadata changed during observation.");
        }
        return planner.Finish(stamp);
    }

    private static void ValidatePlannerAuthority(
        Dictionary<(string Kind, string Id), (long Sequence, AuthorityChange Change)> authority,
        long sequence, AuthorityChange expected)
    {
        if (!authority.TryGetValue((expected.Kind, expected.Id), out var latest)
            || latest.Sequence != sequence || latest.Change != expected)
        {
            throw new InvalidDataException("Retention metadata disagrees with the latest committed typed authority.");
        }
    }
}
