namespace Kora.Windows.Storage;

internal static class HostInteractionSchema
{
    internal const string Partition = "InteractionStorageV1";
    internal const string FileName = "interaction.db";
    internal const int ApplicationId = 1263489587;
    internal const int Version = 7;
    internal static readonly string[] Tables =
    [
        """
        CREATE TABLE work_sessions(
            session_id TEXT PRIMARY KEY NOT NULL,
            generation INTEGER NOT NULL CHECK(generation>0),
            state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 2),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """,
        """
        CREATE TABLE host_observations(
            request_id TEXT PRIMARY KEY NOT NULL,
            session_id TEXT NOT NULL REFERENCES work_sessions(session_id),
            run_id TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK(revision>0),
            generation INTEGER NOT NULL CHECK(generation>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 131072),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """,
        """
        CREATE TABLE host_questions(
            question_id TEXT PRIMARY KEY NOT NULL,
            request_id TEXT NOT NULL,
            session_id TEXT NOT NULL REFERENCES work_sessions(session_id),
            revision INTEGER NOT NULL CHECK(revision>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 131072),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """,
        """
        CREATE TABLE scoped_grants(
            approval_id TEXT PRIMARY KEY NOT NULL,
            session_id TEXT NOT NULL REFERENCES work_sessions(session_id),
            revision INTEGER NOT NULL CHECK(revision>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 131072),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """,
        // No session FK, due date or cascading deletion: minimal grant provenance is independently retained.
        """
        CREATE TABLE perpetual_grants(
            approval_id TEXT PRIMARY KEY NOT NULL,
            revision INTEGER NOT NULL CHECK(revision>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 131072),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """,
        """
        CREATE TABLE security_audit_events(
            sequence INTEGER PRIMARY KEY NOT NULL CHECK(sequence>0),
            correlation_id TEXT UNIQUE NOT NULL,
            previous_hash TEXT NOT NULL CHECK(length(previous_hash)=64),
            hash TEXT NOT NULL CHECK(length(hash)=64),
            envelope TEXT NOT NULL CHECK(length(CAST(envelope AS BLOB)) BETWEEN 1 AND 131072)) STRICT
        """,
        """
        CREATE TABLE authority_head(
            singleton INTEGER PRIMARY KEY NOT NULL CHECK(singleton=1),
            sequence INTEGER NOT NULL CHECK(sequence>=0),
            hash TEXT NOT NULL CHECK(length(hash)=64)) STRICT
        """,
    ];

    internal const string MetadataTable = """
        CREATE TABLE session_metadata(
            session_id TEXT PRIMARY KEY NOT NULL REFERENCES work_sessions(session_id),
            revision INTEGER NOT NULL CHECK(revision>0),
            name TEXT NOT NULL CHECK(length(CAST(name AS BLOB)) BETWEEN 1 AND 480),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """;

    internal static readonly string[] MetadataTables = [.. Tables, MetadataTable];
    internal const string WaitTable = """
        CREATE TABLE host_task_waits(
            task_id TEXT PRIMARY KEY NOT NULL REFERENCES host_tasks(task_id),
            question_id TEXT UNIQUE NOT NULL REFERENCES host_questions(question_id),
            generation INTEGER NOT NULL CHECK(generation>0),
            run_id TEXT NOT NULL,
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """;
    internal const string RunTable = """
        CREATE TABLE host_task_runs(
            task_id TEXT PRIMARY KEY NOT NULL REFERENCES host_tasks(task_id),
            run_id TEXT NOT NULL CHECK(length(run_id)=36)) STRICT
        """;
    internal static readonly string[] AuthorityTables = [.. MetadataTables, .. WindowsSqliteHostTaskStore.Schema, RunTable, WaitTable];
    internal static readonly string[] HistorySchema =
    [
        """
        CREATE TABLE session_history_heads(
            session_id TEXT PRIMARY KEY NOT NULL REFERENCES work_sessions(session_id),
            sequence INTEGER NOT NULL CHECK(sequence>=0)) STRICT
        """,
        """
        CREATE TABLE session_history(
            event_id TEXT UNIQUE NOT NULL,
            session_id TEXT NOT NULL REFERENCES session_history_heads(session_id),
            sequence INTEGER NOT NULL CHECK(sequence>0),
            source TEXT NOT NULL CHECK(length(CAST(source AS BLOB)) BETWEEN 1 AND 131072),
            digest TEXT NOT NULL CHECK(length(digest)=64),
            projection TEXT NOT NULL CHECK(length(CAST(projection AS BLOB)) BETWEEN 1 AND 131072),
            PRIMARY KEY(session_id,sequence)) STRICT
        """,
    ];
    internal const string QueueTable = """
        CREATE TABLE session_queue(
            task_id TEXT PRIMARY KEY NOT NULL REFERENCES host_tasks(task_id),
            session_id TEXT NOT NULL REFERENCES work_sessions(session_id),
            revision INTEGER NOT NULL CHECK(revision>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 4096),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """;
    internal static readonly string[] HistoryTables = [.. AuthorityTables, .. HistorySchema];
    internal static readonly string[] QueueTables = [.. HistoryTables, QueueTable];
    internal const string RetentionTable = """
        CREATE TABLE session_retention(
            session_id TEXT PRIMARY KEY NOT NULL REFERENCES work_sessions(session_id),
            last_activity TEXT NOT NULL,
            archive_days INTEGER NOT NULL CHECK(archive_days>=1),
            delete_days INTEGER NOT NULL CHECK(delete_days>archive_days AND delete_days<=365),
            perpetual INTEGER NOT NULL CHECK(perpetual IN (0,1)),
            purged INTEGER NOT NULL CHECK(purged IN (0,1)),
            exemption_audit INTEGER REFERENCES security_audit_events(sequence)) STRICT
        """;
    internal static readonly string[] RetentionTables = [.. QueueTables, RetentionTable];
    internal const string MemoryProfileTable = """
        CREATE TABLE memory_profile(
            singleton INTEGER PRIMARY KEY NOT NULL CHECK(singleton=1),
            profile_id TEXT NOT NULL CHECK(length(profile_id)=36)) STRICT
        """;
    internal const string MemoryTable = """
        CREATE TABLE reviewed_memory(
            memory_id TEXT PRIMARY KEY NOT NULL,
            session_id TEXT NOT NULL REFERENCES work_sessions(session_id),
            revision INTEGER NOT NULL CHECK(revision>0),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 8192),
            audit_sequence INTEGER NOT NULL REFERENCES security_audit_events(sequence)) STRICT
        """;
    internal static readonly string[] CurrentTables = [.. RetentionTables, MemoryProfileTable, MemoryTable];
}
