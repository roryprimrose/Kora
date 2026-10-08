using System.Diagnostics;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class InteractionStorageFixture : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    internal InteractionStorageFixture()
    {
        ActivitySource.AddActivityListener(listener);
        Tasks = new(Paths);
        Reopen();
    }

    internal OwnedStorageFixture Paths { get; } = new();
    internal WindowsSqliteHostTaskStore Tasks { get; }
    internal WindowsSqliteHostInteractionStore Store { get; private set; } = null!;
    internal HostQuestionService Questions { get; private set; } = null!;
    internal HostAuthorizationService Authorization { get; private set; } = null!;
    internal Clock Time { get; } = new();
    internal HostRequest Request { get; set; } = NewRequest();
    internal HostOperationProposal Proposal { get; set; } = null!;
    internal HostAuthorizationPolicy Policy { get; set; } = new(true, true, false, true);
    internal long ObservationRevision { get; set; }
    internal string DatabasePath => Path.Combine(Paths.LocalRoot, HostInteractionSchema.Partition, HostInteractionSchema.FileName);
    internal CancellationToken Token => TestContext.Current.CancellationToken;

    internal async Task InitializeAsync()
    {
        await Tasks.InitializeAsync(Token);
        await Store.InitializeAsync(Token);
        await AdmitAsync(newSession: true);
    }

    internal async Task AdmitAsync(bool newSession)
    {
        await RunAsync(async () =>
        {
            await Tasks.CommitAsync(new(Request, new(1), HostTaskState.IntentRecorded), 0, Token);
            if (newSession)
            {
                await Store.CreateSessionAsync(Request, Token);
            }
        });
        Proposal = new(Request, new(Guid.NewGuid()), new(1), Binding(), HostOperationEffect.BoundedRead, Time.Now.AddHours(1));
        ObservationRevision = 0;
        await PublishAsync();
    }

    internal void Reopen(IHostInteractionTransactionCheckpoint? checkpoint = null, AuditRetentionPolicy? auditPolicy = null)
    {
        Store = new(Paths, Tasks, Time, checkpoint, auditPolicy: auditPolicy);
        Questions = new(Store, Time);
        Authorization = new(Store, Time);
    }

    internal async Task PublishAsync() =>
        ObservationRevision = (await RunAsync(() => Store.PublishTrustedSnapshotAsync(Request, Policy, Proposal,
            ObservationRevision, Token))).Value;

    internal Task PublishAsyncAfterLifecycle()
    {
        ObservationRevision = 0;
        return PublishAsync();
    }

    internal async Task<T> RunAsync<T>(Func<ValueTask<T>> operation)
    {
        using var host = HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        return await operation();
    }

    internal async Task RunAsync(Func<Task> operation)
    {
        using var host = HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        await operation();
    }

    internal async Task<HostQuestionRecord> PresentAsync() =>
        (await RunAsync(() => Authorization.PresentAsync(Request, Token))).Question
            ?? throw new InvalidOperationException("The fixture approval was not presented.");

    internal async Task<OperationGrant> GrantAsync(string scope)
    {
        var question = await PresentAsync();
        return (await RunAsync(() => Authorization.ApproveAsync(question.Key, new([scope]), RequestOrigin.LocalUi, Token))).Grant
            ?? throw new InvalidOperationException("The fixture approval was not committed.");
    }

    internal SqliteConnection OpenRaw()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }

    internal long Count(string table)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table};";
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException("The fixture count is missing."));
    }

    internal void Mutate(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=OFF;" + sql;
        command.ExecuteNonQuery();
    }

    internal void StageLegacy(int version)
    {
        var events = new List<HostTaskRecord>();
        using (var authority = OpenRaw())
        {
            using var read = authority.CreateCommand();
            read.CommandText = """
                SELECT t.task_id,t.request_id,t.session_id,t.origin,t.invocation_id,e.revision,e.state
                FROM host_tasks t JOIN host_task_events e ON e.task_id=t.task_id ORDER BY t.task_id,e.revision;
                """;
            using var rows = read.ExecuteReader();
            while (rows.Read()) { events.Add(WindowsSqliteHostTaskStore.Decode(rows)); }
        }
        using (var legacy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Paths.LocalRoot, "HostStorageV1", "host.db"),
            Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            legacy.Open();
            using var transaction = legacy.BeginTransaction();
            using var clear = legacy.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = "PRAGMA journal_mode=PERSIST; DELETE FROM host_task_events; DELETE FROM host_tasks; UPDATE task_authority_handoff SET frozen=0;";
            clear.ExecuteNonQuery();
            foreach (var record in events) { WindowsSqliteHostTaskStore.WriteTask(legacy, transaction, record); }
            transaction.Commit();
        }
        Mutate("DROP TABLE host_task_waits; DROP TABLE host_task_runs; DROP TABLE host_task_events; DROP TABLE host_tasks;"
            + (version == 1 ? "DROP TABLE session_metadata;" : string.Empty)
            + "PRAGMA user_version=" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
    }

    public void Dispose()
    {
        listener.Dispose();
        Paths.Dispose();
    }

    internal static HostRequest NewRequest(HostId<SessionIdentity>? session = null,
        RequestOrigin origin = RequestOrigin.LocalUi) =>
        new(new(Guid.NewGuid()), session ?? new(Guid.NewGuid()), new(Guid.NewGuid()), origin, new(Guid.NewGuid()));

    internal static ExactOperationBinding Binding(int changed = -1, string source = "builtin")
    {
        var digests = Enumerable.Range(0, 9).Select(i => new string(i == changed ? 'b' : 'a', 64)).ToArray();
        return new("bounded.read", source, "inspect", digests[0], digests[1], digests[2], digests[3],
            digests[4], digests[5], digests[6], digests[7], digests[8], new(1));
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
