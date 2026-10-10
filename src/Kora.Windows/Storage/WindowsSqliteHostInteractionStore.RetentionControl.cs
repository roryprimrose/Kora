using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionRetentionControlStore
{
    public ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        database.RequireEmptyArtifactInventory();
        foreach (var session in ReadRetentionCandidates(connection, token).Take(MaximumRetentionSessions))
        {
            if (!HasRetentionHold(connection, session))
            {
                throw new InvalidOperationException("Ordinary session retention is due. Content inspection is held until private background maintenance establishes current source availability; no browse-triggered cleanup.");
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence FROM authority_head WHERE singleton=1;";
        return command.ExecuteScalar() is long revision ? revision
            : throw new InvalidDataException("The retention inspection authority revision is unavailable.");
    }, token));

    public ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(
        HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            return ReadRetentionObservation(connection, session);
        }, token));
    }

    private SessionRetentionObservation ReadRetentionObservation(SqliteConnection connection,
        HostId<SessionIdentity> session)
    {
        var row = RequireSession(connection, session);
        var state = ReadRetention(connection, session);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT exemption_audit FROM session_retention WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        var sequence = command.ExecuteScalar();
        return new(state, row.Authority.Generation, sequence is long value ? value : 0,
            HasRetentionHold(connection, session)) { ObservedAt = time.GetUtcNow(), Removed = row.State == 2 };
    }

    public ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(
        HostRequest request, SessionRetentionObservation expected, bool perpetual,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(admitted);
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
            || request.SessionId != expected.State.SessionId)
        {
            throw new InvalidOperationException("Retention control requires original native input for the exact displayed session ID, without model/tool invocation authority.");
        }
        return RunHostMutationAsync(request, "session.retention.perpetual",
            (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var row = RequireSession(connection, request.SessionId);
            var current = ReadRetentionObservation(connection, request.SessionId);
            if (row.State == 2 || current.State.Purged || current.Generation != expected.Generation
                || current.State != expected.State || current.ExemptionAuditSequence != expected.ExemptionAuditSequence)
            {
                throw new InvalidOperationException("Retention preview changed or the session was removed. Refresh and review the exact dates again.");
            }
            var sequence = AppendAudit(connection, transaction, intent, row.Authority, audit,
                changes: [ExemptionChange(request.SessionId, perpetual)]);
            Execute(connection, transaction,
                "UPDATE session_retention SET perpetual=$perpetual,exemption_audit=$audit WHERE session_id=$id;",
                ("$perpetual", perpetual ? 1 : 0), ("$audit", sequence), ("$id", Id(request.SessionId)));
            return new SessionRetentionObservation(current.State with { Perpetual = perpetual },
                current.Generation, sequence, current.WorkHoldObserved) { ObservedAt = time.GetUtcNow() };
        }, token, admitted, requireIdle: false);
    }
}
