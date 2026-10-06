using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteTaskInterruptionTests
{
    private const string RootVariable = "KORA_OWNED_STORAGE_CHILD_ROOT";
    private const string ModeVariable = "KORA_OWNED_STORAGE_CHILD_MODE";
    private const string TaskVariable = "KORA_OWNED_STORAGE_CHILD_TASK";
    private const string RequestVariable = "KORA_OWNED_STORAGE_CHILD_REQUEST";
    private const string SessionVariable = "KORA_OWNED_STORAGE_CHILD_SESSION";

    [Theory]
    [InlineData("intent", HostTaskState.Interrupted)]
    [InlineData("dispatch", HostTaskState.Unknown)]
    [InlineData("uncommitted", HostTaskState.Interrupted)]
    public async Task Owned_child_interruption_recovers_atomic_receipts_without_replaying_work(
        string mode, HostTaskState recoveredState)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var store = new WindowsSqliteHostTaskStore(fixture);
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        if (string.Equals(mode, "intent", StringComparison.Ordinal))
        {
            await store.InitializeAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        }
        var journalPath = Path.Combine(fixture.LocalRoot, "HostStorageV1", "host.db-journal");
        var journalPermissions = new FileInfo(journalPath).GetAccessControl().GetSecurityDescriptorBinaryForm();
        var marker = Path.Combine(fixture.LocalRoot, "child-ready");
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteTaskInterruptionTests),
            nameof(Fixture_owned_child_storage_only));
        start.Environment[RootVariable] = fixture.LocalRoot;
        start.Environment[ModeVariable] = mode;
        start.Environment[TaskVariable] = request.TaskId.Value.ToString("D");
        start.Environment[RequestVariable] = request.RequestId.Value.ToString("D");
        start.Environment[SessionVariable] = request.SessionId.Value.ToString("D");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The fixture-owned child could not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(marker))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"The storage-only child exited before its checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            if (string.Equals(mode, "uncommitted", StringComparison.Ordinal))
            {
                new FileInfo(journalPath)
                    .Length.Should().BeGreaterThan(512, "the synthetic child must have flushed a hot rollback journal");
            }
            // The only process killed is the exact child just created by this synthetic fixture.
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            var reopened = new WindowsSqliteHostTaskStore(fixture);
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            new FileInfo(journalPath).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(journalPermissions);
            var prior = await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken);
            prior.Should().NotBeNull();
            prior!.State.Should().Be(string.Equals(mode, "dispatch", StringComparison.Ordinal)
                ? HostTaskState.DispatchRecorded : HostTaskState.IntentRecorded);
            prior.Revision.Value.Should().Be(string.Equals(mode, "dispatch", StringComparison.Ordinal) ? 2 : 1);
            using (var recovery = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Recovery))
            {
                await reopened.CommitAsync(prior.Recover(), prior.Revision.Value, TestContext.Current.CancellationToken);
            }
            var receipt = await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken);
            receipt!.State.Should().Be(recoveredState);
            (await reopened.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().BeEmpty();
            // Reopening and reading cannot dispatch anything or append a second recovery/terminal event.
            (await new WindowsSqliteHostTaskStore(fixture).ReadTaskAsync(request.TaskId,
                TestContext.Current.CancellationToken)).Should().Be(receipt);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(fixture.LocalRoot, "HostStorageV1", "host.db"),
                Mode = SqliteOpenMode.ReadWrite, Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM host_task_events;";
            command.ExecuteScalar().Should().Be(receipt.Revision.Value);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
            await Task.WhenAll(output, error);
        }
    }

    [WindowsFact]
    public async Task Fixture_owned_child_storage_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null)
        {
            return;
        }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        if (!Guid.TryParseExact(Environment.GetEnvironmentVariable(TaskVariable), "D", out var task)
            || !Guid.TryParseExact(Environment.GetEnvironmentVariable(RequestVariable), "D", out var request)
            || !Guid.TryParseExact(Environment.GetEnvironmentVariable(SessionVariable), "D", out var session))
        {
            throw new InvalidOperationException("The child may only use its explicitly owned scratch fixture.");
        }
        var paths = new ExistingPaths(root);
        using var listener = Listen();
        var store = new WindowsSqliteHostTaskStore(paths);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        var intent = await store.ReadTaskAsync(new(task), TestContext.Current.CancellationToken);
        if (intent is null && string.Equals(mode, "intent", StringComparison.Ordinal))
        {
            intent = new HostTaskRecord(new HostRequest(new(request), new(session), new(task),
                RequestOrigin.HostSystem), new(1), HostTaskState.IntentRecorded);
        }
        intent = intent ?? throw new InvalidDataException("The synthetic child intent is missing.");
        using var host = HostActivity.BeginRoot(intent.Request, HostActivityLayer.Application, HostOperation.Request);
        if (string.Equals(mode, "intent", StringComparison.Ordinal))
        {
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        }
        if (string.Equals(mode, "dispatch", StringComparison.Ordinal))
        {
            await store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), intent.Revision.Value,
                TestContext.Current.CancellationToken);
        }
        if (string.Equals(mode, "uncommitted", StringComparison.Ordinal))
        {
            var directory = new RestrictedStorageDirectory(paths, includeKeys: false);
            using var lease = directory.AcquireLease();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(root, "HostStorageV1", "host.db"),
                Mode = SqliteOpenMode.ReadWrite, Pooling = false,
            }.ToString());
            connection.Open();
            using var configure = connection.CreateCommand();
            configure.CommandText = "PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL; PRAGMA cache_size=1; PRAGMA cache_spill=ON;";
            configure.ExecuteNonQuery();
            using var transaction = connection.BeginTransaction();
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO host_task_events(task_id,revision,state) VALUES($task,2,1);
                UPDATE host_tasks SET revision=2,state=1 WHERE task_id=$task;
                """;
            write.Parameters.AddWithValue("$task", task.ToString("D"));
            write.ExecuteNonQuery();
            await SignalAndWait(root);
        }
        else if (mode is "intent" or "dispatch")
        {
            await SignalAndWait(root);
        }
        else
        {
            throw new InvalidOperationException("An unknown storage-only child mode was requested.");
        }
    }

    private static async Task SignalAndWait(string root)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "child-ready"), "synthetic storage checkpoint",
            TestContext.Current.CancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot { get; } = root;
        public string RoamingRoot => Path.Combine(LocalRoot, "UnusedRoaming");
    }
}
