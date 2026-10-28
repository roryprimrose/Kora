using Kora.Core.Authorization;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private ValueTask<HostTaskRecord> AdmitOriginalControlDispatchAsync(HostRequest request, HostRevision generation,
        Func<bool> eligible, string action, string source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Original control dispatch requires original local input.");
        }
        return RunHostMutationAsync(request, action, (connection, transaction, intent, audit) =>
        {
            var session = RequireSession(connection, request.SessionId).Authority;
            if (!session.IsActive || session.Generation != generation
                || intent.Request != request || intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1
                || ReadRun(connection, request.TaskId) != runId || ReadWait(connection, request.TaskId) is not null
                || ObserveConversationTask(connection, intent, session) is not null
                || ReadDispatchAdmission(connection, request.TaskId, LocalContextAdmissionAction) is not null
                || ReadDispatchAdmission(connection, request.TaskId, BundledControlAdmissionAction) is not null)
            {
                throw new InvalidOperationException("The exact original " + source + " intent or active generation changed.");
            }
            var dispatched = intent.Next(HostTaskState.DispatchRecorded);
            AppendAudit(connection, transaction, intent, session, audit, changes: [TaskChange(dispatched)]);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, dispatched);
            return dispatched;
        }, token, eligible);
    }

    private HostTaskObservation? ObserveOriginalControlTask(SqliteConnection connection,
        HostTaskRecord task, WorkSessionAuthorization session, string action, string source)
    {
        var admitted = ReadDispatchAdmission(connection, task.Request.TaskId, action);
        if (admitted is null) { return null; }
        var dispatch = new HostTaskRecord(task.Request, new(2), HostTaskState.DispatchRecorded);
        if (admitted.Request != task.Request || admitted.IntentRevision.Value != 1
            || admitted.Audit.Category != SecurityAuditCategory.SecurityApproval
            || admitted.Audit.Outcome != SecurityAuditOutcome.Succeeded
            || task.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !admitted.Changes.Contains(TaskChange(dispatch)) || task.Revision.Value < 2)
        {
            throw new InvalidDataException("Original " + source + " authority does not bind the exact durable dispatch.");
        }
        return new(task, admitted.SessionGeneration, source,
            ReadRun(connection, task.Request.TaskId) == runId && admitted.SessionGeneration == session.Generation, null);
    }

    private static AuthorityAudit? ReadDispatchAdmission(SqliteConnection connection, HostId<TaskIdentity> task, string action)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT envelope FROM security_audit_events
            WHERE json_extract(envelope,'$.Audit.ActionId')=$action
                AND json_extract(envelope,'$.Request.TaskId.Value')=$task
            ORDER BY sequence LIMIT 2;
            """;
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$task", Id(task));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var admitted = HostInteractionCodec.Decode<AuthorityAudit>(reader.GetString(0));
        if (reader.Read()) { throw new InvalidDataException("An original control dispatch cannot be admitted more than once."); }
        return admitted;
    }
}