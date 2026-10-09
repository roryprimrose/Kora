using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Memory;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

/// <summary>
/// Host-only durable authority. This stores decisions, not executable tokens or effect receipts.
/// Tasks, questions and required authority audit share one lease and transaction.
/// The legacy task ledger is frozen before validated schema migration; it is never an execution source afterwards.
/// </summary>
public sealed partial class WindowsSqliteHostInteractionStore : IHostInteractionStore, ISessionWorkspaceStore, ISessionHistoryStore, ISessionQueueStore, ISessionWorkStore, ISessionRetentionStore, ICommittedAuthorityAuditReader, IMemoryStore
{
    private static readonly string EmptyHash = new('0', 64);
    private readonly RestrictedSqliteDatabase database;
    private readonly WindowsSqliteHostTaskStore tasks;
    private readonly Guid runId = Guid.NewGuid();
    private readonly TimeProvider time;
    private readonly EvidenceRetentionPolicy retentionPolicy;
    private readonly AuditRetentionPolicy? auditPolicy;
    private readonly SessionRetentionPolicy sessionRetentionPolicy;
    private readonly IApplicationDataPaths paths;
    private readonly HashSet<HostId<SessionIdentity>> liveControlSessions = [];
    private readonly IHostInteractionTransactionCheckpoint? checkpoint;
    private string? inspectionIdentity;

    public WindowsSqliteHostInteractionStore(IApplicationDataPaths paths, WindowsSqliteHostTaskStore tasks,
        TimeProvider? timeProvider = null, EvidenceRetentionPolicy? retentionPolicy = null,
        AuditRetentionPolicy? auditPolicy = null, SessionRetentionPolicy? sessionRetentionPolicy = null)
        : this(paths, tasks, timeProvider, checkpoint: null, retentionPolicy, auditPolicy, sessionRetentionPolicy)
    {
    }

    internal WindowsSqliteHostInteractionStore(IApplicationDataPaths paths, WindowsSqliteHostTaskStore tasks,
        TimeProvider? timeProvider, IHostInteractionTransactionCheckpoint? checkpoint,
        EvidenceRetentionPolicy? retentionPolicy = null, AuditRetentionPolicy? auditPolicy = null,
        SessionRetentionPolicy? sessionRetentionPolicy = null)
    {
        this.tasks = tasks;
        this.paths = paths;
        time = timeProvider ?? TimeProvider.System;
        this.retentionPolicy = retentionPolicy ?? new EvidenceRetentionPolicy();
        this.auditPolicy = auditPolicy;
        this.sessionRetentionPolicy = sessionRetentionPolicy ?? new();
        this.checkpoint = checkpoint;
        database = new(paths, HostInteractionSchema.Partition, HostInteractionSchema.FileName,
            HostInteractionSchema.ApplicationId, HostInteractionSchema.CurrentTables,
            new(1, 2, HostInteractionSchema.Tables, MigrateMetadata),
            new(2, 3, HostInteractionSchema.MetadataTables, ConsolidateTasks),
            new(3, 4, HostInteractionSchema.AuthorityTables, MigrateHistory),
            new(4, 5, HostInteractionSchema.HistoryTables, MigrateQueue),
            new(5, 6, HostInteractionSchema.QueueTables, MigrateRetention),
            memoryMigration: new(6, HostInteractionSchema.Version, HostInteractionSchema.RetentionTables, MigrateMemory));
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            if (!database.HasExistingPartition()) { tasks.RequireFreshAuthority(cancellationToken); }
            using var lease = database.AcquireLease(out var created, cancellationToken);
            using var connection = Open(created, cancellationToken);
            tasks.BindAuthority(database, ValidateAuthority, runId, ObserveTaskActivity);
            var identity = database.ReadIdentity();
            if (inspectionIdentity is not null && !Same(inspectionIdentity, identity))
            {
                throw new InvalidDataException("The initialized authority store was replaced.");
            }
            inspectionIdentity = identity;
        }, cancellationToken));

    private void ConsolidateTasks(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAudit(connection);
        ValidateRows(connection);
        ValidateMetadata(connection);
        Execute(connection, transaction, string.Join(';', WindowsSqliteHostTaskStore.Schema) + ";"
            + HostInteractionSchema.RunTable + ";" + HostInteractionSchema.WaitTable);
        tasks.ImportAuthority(connection, transaction, token);
        ValidateConsolidatedAuthority(connection);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<WorkSessionAuthorization?> ReadSessionAsync(HostId<SessionIdentity> sessionId,
        CancellationToken cancellationToken)
    {
        sessionId.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            var session = ReadSession(connection, sessionId);
            return session?.Authority;
        }, cancellationToken));
    }

    /// <summary>Passive typed history only; reading never refreshes a session or admits a reply or effect.</summary>
    public ValueTask<ImmutableArray<HostQuestionRecord>> ReadQuestionsAsync(HostId<SessionIdentity> sessionId,
        CancellationToken cancellationToken)
    {
        sessionId.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            return ReadQuestions(connection).Where(q => q.Key.Request.SessionId == sessionId).ToImmutableArray();
        }, cancellationToken));
    }

    public ValueTask<ImmutableArray<OperationGrant>> ReadGrantsAsync(CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            return ReadGrants(connection);
        }, cancellationToken));

    public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        WindowsSqliteHostTaskStore.ValidatePage(after, limit);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_id,generation,state FROM work_sessions WHERE state<>2 AND session_id>$after ORDER BY session_id LIMIT $limit;";
            command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
            command.Parameters.AddWithValue("$limit", limit + 1);
            using var reader = command.ExecuteReader();
            var rows = new List<WorkSessionAuthorization>();
            while (reader.Read()) { rows.Add(DecodeSession(reader).Authority); }
            database.VerifyFiles();
            return new SessionPage<WorkSessionAuthorization>([.. rows.Take(limit)],
                rows.Count > limit ? rows[limit - 1].SessionId.Value : null);
        }, cancellationToken));
    }

    public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session,
        Guid? after, int limit, CancellationToken cancellationToken)
    {
        session.Validate();
        WindowsSqliteHostTaskStore.ValidatePage(after, limit);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            var rows = ReadQuestions(connection, session, after, limit + 1);
            database.VerifyFiles();
            return new SessionPage<HostQuestionRecord>([.. rows.Take(limit)],
                rows.Length > limit ? rows[limit - 1].Key.QuestionId.Value : null);
        }, cancellationToken));
    }

    public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session,
        Guid? after, int limit, CancellationToken cancellationToken) =>
        tasks.ReadSessionPageAsync(session, after, limit, cancellationToken);

    public async ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken)
    {
        RequireLive(request);
        await Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await tasks.CommitControlIntentAsync(intent, cancellationToken).ConfigureAwait(false);
        return intent;
    }

    public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request,
        HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canControl);
        return SetLifecycleAsync(request, expectedGeneration, active, remove: false, canControl, cancellationToken);
    }

    public ValueTask<WorkSessionAuthorization> CreateSessionAsync(HostRequest request,
        CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, "session.create", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null)
            {
                throw new InvalidOperationException("The durable work-session identity already exists.");
            }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            return session;
        }, cancellationToken);

    /// <summary>Done/resume invalidates questions and scoped grants. Removed identities are tombstoned, never reused.</summary>
    public ValueTask<WorkSessionAuthorization> SetSessionLifecycleAsync(HostRequest request,
        HostRevision expectedGeneration, bool active, bool remove, CancellationToken cancellationToken)
    {
        if (remove)
        {
            throw new InvalidOperationException("Removal requires the guarded explicit disposition preview and confirmation path.");
        }
        return SetLifecycleAsync(request, expectedGeneration, active, remove: false, canControl: null, cancellationToken);
    }

    private ValueTask<WorkSessionAuthorization> SetLifecycleAsync(HostRequest request,
        HostRevision expectedGeneration, bool active, bool remove, Func<bool>? canControl, CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, remove ? "session.remove" : active ? "session.resume" : "session.done",
            (connection, transaction, intent, audit) =>
            {
                var previous = RequireSession(connection, request.SessionId);
                if (canControl is not null && (!canControl() || ReadQuestions(connection, request.SessionId)
                    .Any(q => q.Status == QuestionStatus.Pending)))
                {
                    throw new InvalidOperationException("Session lifecycle requires current host gates and no unresolved questions.");
                }
                if (previous.Authority.Generation != expectedGeneration || previous.State == 2
                    || (active && remove) || (!remove && previous.Authority.IsActive == active))
                {
                    throw new InvalidOperationException("The durable work-session lifecycle conflicts.");
                }
                var session = new WorkSessionAuthorization(request.SessionId,
                    new(checked(expectedGeneration.Value + 1)), active);
                var state = remove ? 2 : active ? 0 : 1;
                var questions = ReadQuestions(connection).Where(q => q.Key.Request.SessionId == request.SessionId
                    && q.Status == QuestionStatus.Pending).Select(question => question with
                    {
                        Key = question.Key.Next(), Status = QuestionStatus.Cancelled,
                    }).ToArray();
                var grants = ReadGrants(connection).Where(g => g.Scope != OperationGrantScope.Perpetual
                    && g.ApprovedProposal.Request.SessionId == request.SessionId && g.Status == OperationGrantStatus.Active)
                    .Select(grant => EndGrant(grant, "work-session-ended")).ToArray();
                var changes = new List<AuthorityChange> { SessionChange(session, state) };
                changes.AddRange(questions.Select(QuestionChange));
                changes.AddRange(grants.Select(GrantChange));
                var memories = MemoryInvalidations(connection, request.SessionId);
                changes.AddRange(memories.Select(MemoryChange));
                var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: changes);
                WriteSession(connection, transaction, session, state, sequence);
                foreach (var memory in memories) { WriteMemory(connection, transaction, memory, sequence); }
                foreach (var question in questions)
                {
                    WriteQuestion(connection, transaction, question, sequence);
                }
                foreach (var grant in grants)
                {
                    WriteGrant(connection, transaction, grant, sequence);
                }
                Execute(connection, transaction, "DELETE FROM host_observations WHERE session_id=$id;", ("$id", Id(request.SessionId)));
                if (remove)
                {
                    Execute(connection, transaction, """
                        DELETE FROM host_questions WHERE session_id=$id;
                        DELETE FROM scoped_grants WHERE session_id=$id;
                        """, ("$id", Id(request.SessionId)));
                }
                return session;
            }, cancellationToken, canControl);

    /// <summary>
    /// Publish only host-resolved fresh policy/content/identity snapshots, before exposing changed content.
    /// The optimistic observation revision prevents stale host workers overwriting newer snapshots.
    /// Each adapter lifetime must re-admit snapshots; persisted observations alone confer no restart authority.
    /// </summary>
    public ValueTask<HostRevision> PublishTrustedSnapshotAsync(HostRequest request, HostAuthorizationPolicy policy,
        HostOperationProposal? proposal, long expectedObservationRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (expectedObservationRevision < 0 || (proposal is not null && proposal.Request != request))
        {
            throw new ArgumentException("The trusted proposal must own the exact request and observation revision.", nameof(proposal));
        }
        return RunHostMutationAsync(request, "authority.snapshot", (connection, transaction, intent, audit) =>
        {
            var session = RequireSession(connection, request.SessionId);
            if (!session.Authority.IsActive)
            {
                throw new InvalidOperationException("An ended work session cannot publish new authority.");
            }
            var previous = ReadObservation(connection, request.RequestId);
            if ((previous?.Revision.Value ?? 0) != expectedObservationRevision
                || (previous is not null && previous.Request != request))
            {
                throw new InvalidOperationException("The trusted host snapshot revision or identity conflicts.");
            }
            var revision = new HostRevision(checked(expectedObservationRevision + 1));
            var observation = new Observation(request, runId, revision, session.Authority.Generation, policy, proposal);
            var grants = ImmutableArray<OperationGrant>.Empty;
            var questions = ImmutableArray<HostQuestionRecord>.Empty;
            if (proposal is not null)
            {
                grants = ReadGrants(connection).Where(g => g.Status == OperationGrantStatus.Active
                    && g.ApprovedProposal.Binding.HasObservedContentChange(proposal.Binding))
                    .Select(grant => EndGrant(grant, "observed-content-change")).ToImmutableArray();
                questions = ReadQuestions(connection).Where(q => q.Status == QuestionStatus.Pending
                    && q.Proposal is { } pending && pending.Binding.HasObservedContentChange(proposal.Binding))
                    .Select(question => question with
                    {
                        Key = question.Key.Next(), Status = QuestionStatus.Cancelled,
                    }).ToImmutableArray();
            }
            var changes = new List<AuthorityChange>
            {
                Change("observation", Id(request.RequestId), revision.Value, HostInteractionCodec.Encode(observation)),
            };
            changes.AddRange(questions.Select(QuestionChange));
            changes.AddRange(grants.Select(GrantChange));
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, audit, changes: changes);
            foreach (var question in questions)
            {
                WriteQuestion(connection, transaction, question, sequence);
            }
            foreach (var grant in grants)
            {
                WriteGrant(connection, transaction, grant, sequence);
            }
            Execute(connection, transaction, """
                INSERT INTO host_observations(request_id,session_id,run_id,revision,generation,payload,audit_sequence)
                VALUES($id,$session,$run,$revision,$generation,$payload,$audit)
                ON CONFLICT(request_id) DO UPDATE SET run_id=excluded.run_id,revision=excluded.revision,
                    generation=excluded.generation,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
                """, ("$id", Id(request.RequestId)), ("$session", Id(request.SessionId)), ("$run", runId.ToString("D")),
                ("$revision", revision.Value), ("$generation", session.Authority.Generation.Value),
                ("$payload", HostInteractionCodec.Encode(observation)), ("$audit", sequence));
            return revision;
        }, cancellationToken);
    }

    public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
        Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        RequireLive(request);
        return new(Task.Run(() => tasks.WithCommittedIntent(request, (connection, intent) =>
        {
            using var storage = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            ValidateAuthority(connection);
            using var transaction = connection.BeginTransaction();
            var session = RequireSession(connection, request.SessionId).Authority;
            if (ReadSession(connection, request.SessionId)!.State == 2)
            {
                throw new InvalidOperationException("A disposed session cannot append interaction records.");
            }
            var observation = ReadObservation(connection, request.RequestId);
            var admitted = observation is not null && observation.RunId == runId
                && observation.Request == request && observation.Generation == session.Generation;
            var snapshot = new HostInteractionSnapshot(intent, session,
                admitted ? observation!.Policy : new(false, false, false, true),
                admitted ? observation!.Proposal : null,
                ReadQuestions(connection).Where(q => q.Key.Request == request).ToImmutableArray(),
                ReadGrants(connection));
            var commit = transition(snapshot);
            ValidateCommit(snapshot, commit, request);
            var questions = commit.Snapshot.Questions.Where(question =>
                snapshot.Questions.FirstOrDefault(q => q.Key.QuestionId == question.Key.QuestionId) != question).ToArray();
            var grants = commit.Snapshot.Grants.Where(grant =>
                snapshot.Grants.FirstOrDefault(g => g.Id == grant.Id) != grant).ToArray();
            var changes = questions.Select(QuestionChange).Concat(grants.Select(GrantChange)).ToArray();
            var sequence = AppendAudit(connection, transaction, intent, session, commit.Audit, commit.Decision, changes);
            foreach (var question in questions)
            {
                WriteQuestion(connection, transaction, question, sequence);
            }
            foreach (var grant in grants)
            {
                WriteGrant(connection, transaction, grant, sequence);
            }
            Commit(transaction, request, cancellationToken);
            storage.Complete(HostOperationOutcome.Completed);
            return commit.Decision;
        }, cancellationToken), cancellationToken));
    }

    private ValueTask<T> RunHostMutationAsync<T>(HostRequest request, string action,
        Func<SqliteConnection, SqliteTransaction, HostTaskRecord, SecurityAuditEvent, T> mutation,
        CancellationToken cancellationToken, Func<bool>? canControl = null, bool requireIdle = true)
    {
        RequireLive(request);
        return new(Task.Run(() =>
        {
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, action,
                SecurityAuditOutcome.Succeeded, request.Origin switch
                {
                    RequestOrigin.LocalUi => SecurityAuditInitiator.TypedCommand,
                    RequestOrigin.ActivatedVoice => SecurityAuditInitiator.VoiceCommand,
                    _ => SecurityAuditInitiator.System,
                }, Id(request.TaskId));
            using var activity = HostActivity.BeginAudit(request, audit);
            try
            {
                T Mutate(SqliteConnection connection, HostTaskRecord intent)
                {
                    using var storage = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
                    ValidateAuthority(connection);
                    using var transaction = connection.BeginTransaction();
                    var value = mutation(connection, transaction, intent, audit);
                    Commit(transaction, request, cancellationToken, canControl);
                    storage.Complete(HostOperationOutcome.Completed);
                    return value;
                }
                var result = canControl is null || !requireIdle
                    ? tasks.WithCommittedIntent(request, Mutate, cancellationToken)
                    : tasks.WithCommittedIdleIntent(request, Mutate, cancellationToken);
                activity.Complete(HostOperationOutcome.Completed);
                return result;
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }, cancellationToken));
    }

    private void Commit(SqliteTransaction transaction,
        HostRequest request, CancellationToken cancellationToken, Func<bool>? canControl = null)
    {
        RequireLive(request);
        checkpoint?.BeforeCommit(transaction.Connection!, transaction);
        database.VerifyFiles();
        cancellationToken.ThrowIfCancellationRequested();
        if (canControl is not null && !canControl())
        {
            throw new InvalidOperationException("Session control admission changed at the commit boundary.");
        }
        try
        {
            transaction.Commit();
        }
        catch (SqliteException exception)
        {
            // Cancellation during COMMIT cannot certify rollback. Never report it as cancellation or admission.
            throw new IOException("Interaction commit certainty was lost; inspect durable authority before retrying.", exception);
        }
    }

    private SqliteConnection Open(bool created, CancellationToken cancellationToken)
    {
        var connection = database.Open(created, cancellationToken);
        try
        {
            if (created)
            {
                tasks.RequireFreshAuthority(cancellationToken);
                using var transaction = connection.BeginTransaction();
                tasks.ImportAuthority(connection, transaction, cancellationToken);
                Execute(connection, transaction, "INSERT INTO authority_head VALUES(1,0,$hash);", ("$hash", EmptyHash));
                SeedMemoryProfile(connection, transaction);
                transaction.Commit();
            }
            tasks.RequireRetiredAuthority(cancellationToken);
            ValidateAuthority(connection);
            tasks.BindAuthority(database, ValidateAuthority, runId, ObserveTaskActivity);
            return connection;
        }

        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ValidateAuthority(SqliteConnection connection)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateRetention(connection);
        ValidateMemory(connection);
    }

    private static void ValidateConsolidatedAuthority(SqliteConnection connection)
    {
        ValidateAudit(connection);
        ValidateRows(connection);
        ValidateMetadata(connection);
        WindowsSqliteHostTaskStore.ValidateTasks(connection);
        ValidateQuestionTasks(connection);
        ValidateWaits(connection);
    }

    private static void RequireLive(HostRequest request)
    {
        var live = HostActivity.RequireCurrent();
        if (live.Request != request || live.Activity!.IsStopped)
        {
            throw new InvalidOperationException("A live exact host request must own the interaction transaction.");
        }
    }

    private static void ValidateCommit(HostInteractionSnapshot before, HostInteractionCommit commit, HostRequest request)
    {
        var after = commit.Snapshot;
        if (after.Intent != before.Intent || after.Session != before.Session || after.Policy != before.Policy
            || after.Proposal != before.Proposal
            || before.Questions.Any(q => !after.Questions.Any(next => next.Key.QuestionId == q.Key.QuestionId))
            || before.Grants.Any(g => !after.Grants.Any(next => next.Id == g.Id))
            || after.Questions.Select(q => q.Key.QuestionId).Distinct().Count() != after.Questions.Length
            || after.Grants.Select(g => g.Id).Distinct().Count() != after.Grants.Length)
        {
            throw new InvalidDataException("The interaction transition changed host authority or evicted records.");
        }
        foreach (var question in after.Questions)
        {
            HostInteractionCodec.Validate(question);
            var previous = before.Questions.FirstOrDefault(q => q.Key.QuestionId == question.Key.QuestionId);
            if (question.Key.Request != request || (previous != question
                && question.Key.Revision.Value != checked((previous?.Key.Revision.Value ?? 0) + 1)))
            {
                throw new InvalidDataException("The interaction question revision or ownership is invalid.");
            }
        }
        foreach (var grant in after.Grants)
        {
            grant.Validate();
            var previous = before.Grants.FirstOrDefault(g => g.Id == grant.Id);
            if ((previous is null && (grant.ApprovedProposal != before.Proposal
                    || grant.SessionGeneration != before.Session.Generation || grant.Revision.Value != 1))
                || (previous is not null && previous != grant
                    && (grant.Revision.Value != checked(previous.Revision.Value + 1)
                        || grant.ApprovedProposal != previous.ApprovedProposal || grant.Scope != previous.Scope
                        || grant.SessionGeneration != previous.SessionGeneration || grant.CreatorChannel != previous.CreatorChannel
                        || grant.CreatedAt != previous.CreatedAt)))
            {
                throw new InvalidDataException("The grant revision, exact binding or immutable provenance changed.");
            }
        }
    }

    private long AppendAudit(SqliteConnection connection, SqliteTransaction transaction, HostTaskRecord intent,
        WorkSessionAuthorization session, SecurityAuditEvent audit, HostInteractionDecision? decision = null,
        IReadOnlyList<AuthorityChange>? changes = null)
    {
        var live = HostActivity.RequireCurrent();
        if (live.Request.RequestId != intent.Request.RequestId || live.CorrelationId != audit.CorrelationId
            || audit.Category != SecurityAuditCategory.SecurityApproval || !Same(audit.TargetId, Id(intent.Request.TaskId)))
        {
            throw new InvalidDataException("Only the live typed host security path may append authority audit.");
        }
        if (audit.ActionId.StartsWith("session.create.", StringComparison.Ordinal)
            && !Same(audit.ActionId, "session.create.named"))
        {
            // Host preference/control collaborators retain exact authority for this run. Never expire it underneath them.
            liveControlSessions.Add(session.SessionId);
        }
        using var head = connection.CreateCommand();
        head.Transaction = transaction;
        head.CommandText = "SELECT sequence,hash FROM authority_head WHERE singleton=1;";
        long sequence;
        string previousHash;
        using (var reader = head.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new InvalidDataException("The interaction audit head is missing.");
            }
            sequence = checked(reader.GetInt64(0) + 1);
            previousHash = reader.GetString(1);
        }
        var trace = live.Activity!;
        var committedAt = time.GetUtcNow();
        var envelope = new AuthorityAudit(live.Request, intent.Revision, session.Generation, audit,
            trace.TraceId.ToHexString(), trace.SpanId.ToHexString(), committedAt,
            auditPolicy is null ? retentionPolicy.AuditDue(committedAt) : auditPolicy.Due(committedAt), decision?.Outcome,
            decision?.Question?.Key.QuestionId, decision?.Question?.Key.Revision,
            decision?.Grant?.Id, decision?.Grant?.Revision, changes?.ToArray() ?? []);
        var json = HostInteractionCodec.Encode(envelope);
        var hash = Hash(sequence, previousHash, json);
        checkpoint?.BeforeAudit(connection, transaction);
        Execute(connection, transaction, """
            INSERT INTO security_audit_events VALUES($sequence,$correlation,$previous,$hash,$envelope);
            UPDATE authority_head SET sequence=$sequence,hash=$hash WHERE singleton=1;
            """, ("$sequence", sequence), ("$correlation", audit.CorrelationId.ToString("D")),
            ("$previous", previousHash), ("$hash", hash), ("$envelope", json));
        if (decision is not null)
        {
            SessionHistoryPersistence.Decision(connection, transaction, live.Request, session.Generation, decision.Outcome, sequence);
            if (decision.Question?.Status == QuestionStatus.Answered
                || decision.Outcome == HostInteractionOutcome.Cancelled
                    && live.Request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            {
                TouchActivity(connection, transaction, session.SessionId);
            }
        }
        return sequence;
    }

    private static void ValidateAudit(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,correlation_id,previous_hash,hash,envelope FROM security_audit_events ORDER BY sequence;";
        long sequence = 0;
        var hash = EmptyHash;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var json = reader.GetString(4);
                var next = checked(sequence + 1);
                var calculated = Hash(next, hash, json);
                _ = DecodeAuthorityAudit(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), json);
                if (reader.GetInt64(0) != next
                    || !Same(reader.GetString(2), hash) || !Same(reader.GetString(3), calculated))
                {
                    throw new InvalidDataException("The typed interaction audit chain or identity is invalid.");
                }

                sequence = next;
                hash = calculated;
            }
        }
        command.CommandText = "SELECT sequence,hash FROM authority_head WHERE singleton=1;";
        using var head = command.ExecuteReader();
        if (!head.Read() || head.GetInt64(0) != sequence || !Same(head.GetString(1), hash) || head.Read())
        {
            throw new InvalidDataException("The interaction audit head disagrees with its ordered chain.");
        }
    }

    private static AuthorityAudit DecodeAuthorityAudit(long sequence, string correlation, string previous,
        string hash, string json)
    {
        var audit = HostInteractionCodec.Decode<AuthorityAudit>(json);
        if (audit.Audit is null || audit.Request is null || audit.TraceId is null || audit.SpanId is null
            || audit.Changes is null || audit.Changes.Any(change => change is null))
        {
            throw new InvalidDataException("The typed interaction audit has missing required fields.");
        }
        if (sequence <= 0 || !Same(correlation, audit.Audit.CorrelationId.ToString("D"))
            || !Same(hash, Hash(sequence, previous, json))
            || audit.IntentRevision.Value <= 0 || audit.SessionGeneration.Value <= 0
            || audit.Outcome is { } outcome && !Enum.IsDefined(outcome)
            || (audit.QuestionId is null) != (audit.QuestionRevision is null)
            || (audit.ApprovalId is null) != (audit.GrantRevision is null)
            || audit.QuestionRevision is { Value: <= 0 } || audit.GrantRevision is { Value: <= 0 }
            || audit.QuestionId?.Value == Guid.Empty || audit.ApprovalId?.Value == Guid.Empty
            || audit.Audit.ApprovalId == Guid.Empty
            || !AuditRetentionDays.IsValidDeadline(audit.CommittedAt, audit.DueAt)
            || !IsHex(audit.TraceId, 32) || !IsHex(audit.SpanId, 16)
            || audit.Audit.Category != SecurityAuditCategory.SecurityApproval
            || !Same(audit.Audit.TargetId, Id(audit.Request.TaskId))
            || audit.Changes.Any(change => change.Digest is null || !IsHex(change.Digest, 64) || change.Revision <= 0
                || change.Kind is not ("session" or "observation" or "question" or "grant" or "metadata" or "task" or "wait" or "queue" or "retention" or "memory")
                || !Guid.TryParseExact(change.Id, "D", out var id) || id == Guid.Empty
                || !Same(change.Id, id.ToString("D"))))
        {
            throw new InvalidDataException("The typed interaction audit chain or identity is invalid.");
        }
        return audit;
    }

    private static void ValidateRows(SqliteConnection connection)
    {
        _ = ReadQuestions(connection);
        _ = ReadGrants(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,generation,state,audit_sequence FROM work_sessions;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var row = DecodeSession(reader);
                ValidateRowAuthority(connection, reader.GetInt64(3), SessionChange(row.Authority, (int)row.State));
            }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='session' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN work_sessions s ON s.session_id=latest.id
            WHERE s.audit_sequence IS NULL OR s.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed session authority or tombstone is missing or stale; replacement is forbidden.");
        }
        command.CommandText = "SELECT request_id,session_id,run_id,revision,generation,payload,audit_sequence FROM host_observations;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var observation = DecodeObservation(reader);
                ValidateRowAuthority(connection, reader.GetInt64(6), Change("observation", Id(observation.Request.RequestId),
                    observation.Revision.Value, reader.GetString(5)));
            }
        }
        command.CommandText = "PRAGMA foreign_key_check;";
        using var violations = command.ExecuteReader();
        if (violations.Read())
        {
            throw new InvalidDataException("Interaction state has missing session or authoritative audit ownership.");
        }
    }

    private static SessionRow RequireSession(SqliteConnection connection, HostId<SessionIdentity> id) =>
        ReadSession(connection, id) ?? throw new InvalidDataException("A durable host-owned work session is required.");

    private static SessionRow? ReadSession(SqliteConnection connection, HostId<SessionIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,generation,state FROM work_sessions WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeSession(reader) : null;
    }

    private static SessionRow DecodeSession(SqliteDataReader reader) =>
        new(new(new(ParseId(reader.GetString(0))), new(reader.GetInt64(1)), reader.GetInt64(2) == 0),
            reader.GetInt64(2));

    private static Observation? ReadObservation(SqliteConnection connection, HostId<RequestIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_id,session_id,run_id,revision,generation,payload FROM host_observations WHERE request_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeObservation(reader) : null;
    }

    private static Observation DecodeObservation(SqliteDataReader reader)
    {
        var value = HostInteractionCodec.Decode<Observation>(reader.GetString(5));
        if (value.Request is null || !Same(Id(value.Request.RequestId), reader.GetString(0)) || !Same(Id(value.Request.SessionId), reader.GetString(1))
            || value.RunId != ParseId(reader.GetString(2)) || value.Revision.Value != reader.GetInt64(3)
            || value.Generation.Value != reader.GetInt64(4) || value.Policy is null
            || (value.Proposal is { } proposal && proposal.Request != value.Request))
        {
            throw new InvalidDataException("The trusted observation identity/projection is invalid.");
        }
        return value;
    }

    private static ImmutableArray<HostQuestionRecord> ReadQuestions(SqliteConnection connection,
        HostId<SessionIdentity>? session = null, Guid? after = null, int? limit = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT question_id,request_id,session_id,revision,payload,audit_sequence FROM host_questions
            WHERE ($session IS NULL OR session_id=$session) AND question_id>$after
            ORDER BY question_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$session", session is { } id ? Id(id) : DBNull.Value);
        command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$limit", limit ?? -1);
        using var reader = command.ExecuteReader();
        var questions = ImmutableArray.CreateBuilder<HostQuestionRecord>();
        while (reader.Read())
        {
            var question = HostInteractionCodec.Decode<HostInteractionCodec.QuestionData>(reader.GetString(4)).ToQuestion();
            if (!Same(Id(question.Key.QuestionId), reader.GetString(0)) || !Same(Id(question.Key.Request.RequestId), reader.GetString(1))
                || !Same(Id(question.Key.Request.SessionId), reader.GetString(2)) || question.Key.Revision.Value != reader.GetInt64(3))
            {
                throw new InvalidDataException("The typed question and indexed identity disagree.");
            }
            ValidateRowAuthority(connection, reader.GetInt64(5), Change("question", Id(question.Key.QuestionId),
                question.Key.Revision.Value, reader.GetString(4)));
            questions.Add(question);
        }
        return questions.ToImmutable();
    }

    private static ImmutableArray<OperationGrant> ReadGrants(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT approval_id,revision,payload,0,session_id,audit_sequence FROM scoped_grants
            UNION ALL SELECT approval_id,revision,payload,1,NULL,audit_sequence FROM perpetual_grants ORDER BY approval_id;
            """;
        using var reader = command.ExecuteReader();
        var grants = ImmutableArray.CreateBuilder<OperationGrant>();
        while (reader.Read())
        {
            var grant = HostInteractionCodec.Decode<OperationGrant>(reader.GetString(2));
            grant.Validate();
            if (!Same(Id(grant.Id), reader.GetString(0)) || grant.Revision.Value != reader.GetInt64(1)
                || (grant.Scope == OperationGrantScope.Perpetual) != (reader.GetInt64(3) == 1)
                || (grant.Scope != OperationGrantScope.Perpetual && !Same(Id(grant.ApprovedProposal.Request.SessionId), reader.GetString(4)))
                || grants.Any(g => g.Id == grant.Id))
            {
                throw new InvalidDataException("The typed grant scope/provenance projection is invalid.");
            }
            ValidateRowAuthority(connection, reader.GetInt64(5), Change("grant", Id(grant.Id), grant.Revision.Value, reader.GetString(2)));
            grants.Add(grant);
        }
        return grants.ToImmutable();
    }

    private void WriteSession(SqliteConnection connection, SqliteTransaction transaction,
        WorkSessionAuthorization session, int state, long sequence)
    {
        Execute(connection, transaction, """
            INSERT INTO work_sessions VALUES($id,$generation,$state,$audit)
            ON CONFLICT(session_id) DO UPDATE SET generation=excluded.generation,state=excluded.state,audit_sequence=excluded.audit_sequence;
            """, ("$id", Id(session.SessionId)), ("$generation", session.Generation.Value), ("$state", state), ("$audit", sequence));
        SessionHistoryPersistence.Seed(connection, transaction, session.SessionId, session.Generation, baseline: false);
        SeedActivity(connection, transaction, session.SessionId);
        if (state == 0) { TouchActivity(connection, transaction, session.SessionId); }
    }

    private static void WriteQuestion(SqliteConnection connection, SqliteTransaction transaction,
        HostQuestionRecord question, long sequence)
    {
        var written = Execute(connection, transaction, """
            INSERT INTO host_questions VALUES($id,$request,$session,$revision,$payload,$audit)
            ON CONFLICT(question_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence
            WHERE host_questions.request_id=excluded.request_id AND host_questions.session_id=excluded.session_id
                AND host_questions.revision+1=excluded.revision;
            """, ("$id", Id(question.Key.QuestionId)), ("$request", Id(question.Key.Request.RequestId)),
            ("$session", Id(question.Key.Request.SessionId)), ("$revision", question.Key.Revision.Value),
            ("$payload", HostInteractionCodec.Encode(HostInteractionCodec.QuestionData.From(question))), ("$audit", sequence));
        if (written != 1)
        {
            throw new InvalidDataException("The durable question identity or revision conflicts with another record.");
        }
        SessionHistoryPersistence.Question(connection, transaction, question, sequence);
    }

    private static void WriteGrant(SqliteConnection connection, SqliteTransaction transaction, OperationGrant grant, long sequence)
    {
        var perpetual = grant.Scope == OperationGrantScope.Perpetual;
        var table = perpetual ? "perpetual_grants" : "scoped_grants";
        var written = Execute(connection, transaction,
            $"INSERT INTO {table}(approval_id,revision,payload,audit_sequence{(perpetual ? "" : ",session_id")}) "
            + $"VALUES($id,$revision,$payload,$audit{(perpetual ? "" : ",$session")}) "
            + "ON CONFLICT(approval_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence "
            + $"WHERE {table}.revision+1=excluded.revision;",
            ("$id", Id(grant.Id)), ("$revision", grant.Revision.Value), ("$payload", HostInteractionCodec.Encode(grant)),
            ("$audit", sequence), ("$session", Id(grant.ApprovedProposal.Request.SessionId)));
        if (written != 1)
        {
            throw new InvalidDataException("The durable grant revision conflicts.");
        }
    }

    private static OperationGrant EndGrant(OperationGrant grant, string reason) => grant with
    {
        Revision = new(checked(grant.Revision.Value + 1)), Status = OperationGrantStatus.Revoked, RevocationReason = reason,
    };

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        try
        {
            return command.ExecuteNonQuery();
        }
        catch (SqliteException exception)
        {
            throw new IOException("The authoritative interaction write failed; no decision was admitted.", exception);
        }
    }

    private static string Id<T>(HostId<T> id) => id.Value.ToString("D");
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    private static bool IsHex(string text, int length) =>
        text.Length == length && text.Any(c => c != '0') && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Guid ParseId(string text) =>
        Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && Same(text, id.ToString("D"))
            ? id : throw new InvalidDataException("A durable authority identity is invalid.");
    private static string Hash(long sequence, string previous, string envelope) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            sequence.ToString(CultureInfo.InvariantCulture) + "\n" + previous + "\n" + envelope)));

    private sealed record SessionRow(WorkSessionAuthorization Authority, long State);
    private sealed record Observation(HostRequest Request, Guid RunId, HostRevision Revision,
        HostRevision Generation, HostAuthorizationPolicy Policy, HostOperationProposal? Proposal);
    private sealed record AuthorityAudit(HostRequest Request, HostRevision IntentRevision, HostRevision SessionGeneration,
        SecurityAuditEvent Audit, string TraceId, string SpanId, DateTimeOffset CommittedAt, DateTimeOffset DueAt,
        HostInteractionOutcome? Outcome, HostId<QuestionIdentity>? QuestionId, HostRevision? QuestionRevision,
        HostId<ApprovalIdentity>? ApprovalId, HostRevision? GrantRevision, AuthorityChange[] Changes);

    private sealed record AuthorityChange(string Kind, string Id, long Revision, string Digest);

    private static AuthorityChange Change(string kind, string id, long revision, string payload) =>
        new(kind, id, revision, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    private static AuthorityChange SessionChange(WorkSessionAuthorization session, int state) =>
        Change("session", Id(session.SessionId), session.Generation.Value,
            HostInteractionCodec.Encode(new SessionRow(session, state)));
    private static AuthorityChange QuestionChange(HostQuestionRecord question) =>
        Change("question", Id(question.Key.QuestionId), question.Key.Revision.Value,
            HostInteractionCodec.Encode(HostInteractionCodec.QuestionData.From(question)));
    private static AuthorityChange GrantChange(OperationGrant grant) =>
        Change("grant", Id(grant.Id), grant.Revision.Value, HostInteractionCodec.Encode(grant));

    private static void ValidateRowAuthority(SqliteConnection connection, long sequence, AuthorityChange change)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", sequence);
        var json = command.ExecuteScalar() as string
            ?? throw new InvalidDataException("The state record is missing its authoritative audit commit.");
        if (!HostInteractionCodec.Decode<AuthorityAudit>(json).Changes.Contains(change))
        {
            throw new InvalidDataException("The state record is not bound to its typed authoritative audit commit.");
        }
    }
}
