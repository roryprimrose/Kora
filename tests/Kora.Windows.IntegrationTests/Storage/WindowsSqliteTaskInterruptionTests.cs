using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteTaskInterruptionTests
{
    private const string RootVariable = "KORA_OWNED_STORAGE_CHILD_ROOT";
    private const string ModeVariable = "KORA_OWNED_STORAGE_CHILD_MODE";
    private const string TaskVariable = "KORA_OWNED_STORAGE_CHILD_TASK";
    private const string RequestVariable = "KORA_OWNED_STORAGE_CHILD_REQUEST";
    private const string SessionVariable = "KORA_OWNED_STORAGE_CHILD_SESSION";

    [Theory]
    [InlineData("intent", HostTaskState.IntentRecorded, HostTaskState.Interrupted)]
    [InlineData("dispatch", HostTaskState.DispatchRecorded, HostTaskState.Unknown)]
    [InlineData("terminal", HostTaskState.Succeeded, HostTaskState.Succeeded)]
    [InlineData("uncommitted-intent", null, null)]
    [InlineData("uncommitted-dispatch", HostTaskState.IntentRecorded, HostTaskState.Interrupted)]
    [InlineData("uncommitted-terminal", HostTaskState.DispatchRecorded, HostTaskState.Unknown)]
    [InlineData("uncommitted-recovery", HostTaskState.DispatchRecorded, HostTaskState.Unknown)]
    [InlineData("committed-recovery", HostTaskState.Unknown, HostTaskState.Unknown)]
    public async Task Owned_child_interruption_recovers_atomic_receipts_without_replaying_work(
        string mode, HostTaskState? priorState, HostTaskState? recoveredState)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var store = new WindowsSqliteHostTaskStore(fixture);
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        new WindowsSqliteEvidenceSink(fixture).Initialize();
        if (mode is not ("intent" or "uncommitted-intent"))
        {
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
            if (mode is "terminal" or "uncommitted-terminal" or "uncommitted-recovery" or "committed-recovery")
            {
                await store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), 1, TestContext.Current.CancellationToken);
            }
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
            if (mode.StartsWith("uncommitted-", StringComparison.Ordinal))
            {
                OwnedStorageChildProcess.AssertHotJournal(journalPath);
            }
            // The only process killed is the exact child just created by this synthetic fixture.
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            var reopened = new WindowsSqliteHostTaskStore(fixture);
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            new FileInfo(journalPath).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(journalPermissions);
            var prior = await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken);
            (prior?.State).Should().Be(priorState);
            using var recovery = new StorageRecoveryFixture(fixture, reopened);
            var recovered = await recovery.Recovery.RecoverAsync(TestContext.Current.CancellationToken);
            if (prior is { IsTerminal: false })
            {
                recovered.Should().ContainSingle().Which.State.Should().Be(recoveredState);
                recovered[0].Revision.Value.Should().Be(prior.Revision.Value + 1);
            }
            else
            {
                recovered.Should().BeEmpty();
            }
            var receipt = await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken);
            (receipt?.State).Should().Be(recoveredState);
            (await reopened.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await recovery.Recovery.RecoverAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
            recovery.Gaps.Should().BeEmpty();
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
            command.ExecuteScalar().Should().Be(receipt?.Revision.Value ?? 0);
            using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
                Mode = SqliteOpenMode.ReadWrite, Pooling = false,
            }.ToString());
            evidence.Open();
            using var audits = evidence.CreateCommand();
            audits.CommandText = "SELECT action_id,audit_outcome,request_id,session_id,task_id,reason_code FROM security_audit_events;";
            using var rows = audits.ExecuteReader();
            var count = 0;
            while (rows.Read())
            {
                rows.GetString(0).Should().Be("host.task.recovery");
                rows.GetInt64(1).Should().Be((long)SecurityAuditOutcome.Unknown);
                rows.GetString(2).Should().Be(request.RequestId.Value.ToString("D"));
                rows.GetString(3).Should().Be(request.SessionId.Value.ToString("D"));
                rows.GetString(4).Should().Be(request.TaskId.Value.ToString("D"));
                rows.GetString(5).Should().Be(recoveredState == HostTaskState.Interrupted
                    ? "intent-interrupted" : "dispatch-unverified");
                count++;
            }
            count.Should().Be(mode is "terminal" or "uncommitted-intent" ? 0
                : string.Equals(mode, "uncommitted-recovery", StringComparison.Ordinal) ? 2 : 1);
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
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("intent" or "dispatch" or "terminal" or "uncommitted-intent"
            or "uncommitted-dispatch" or "uncommitted-terminal" or "uncommitted-recovery" or "committed-recovery"))
        {
            throw new InvalidOperationException("An unknown storage-only child mode was requested.");
        }
        var checkpoint = mode.StartsWith("uncommitted-", StringComparison.Ordinal)
            ? new SqliteTransactionCheckpoint
            {
                Write = SqliteTransactionCheckpoint.SpillPages,
                Commit = (_, _) => OwnedStorageChildProcess.SignalAndBlock(root, "child-ready"),
            } : null;
        var store = new WindowsSqliteHostTaskStore(paths, checkpoint);
        var intent = await store.ReadTaskAsync(new(task), TestContext.Current.CancellationToken);
        if (intent is null && mode is "intent" or "uncommitted-intent")
        {
            intent = new HostTaskRecord(new HostRequest(new(request), new(session), new(task),
                RequestOrigin.HostSystem), new(1), HostTaskState.IntentRecorded);
        }
        intent = intent ?? throw new InvalidDataException("The synthetic child intent is missing.");
        if (mode is "uncommitted-recovery" or "committed-recovery")
        {
            using var recovery = new StorageRecoveryFixture(paths, store);
            (await recovery.Recovery.RecoverAsync(TestContext.Current.CancellationToken)).Should().ContainSingle();
        }
        else
        {
            using var host = HostActivity.BeginRoot(intent.Request, HostActivityLayer.Application, HostOperation.Request);
            var next = mode switch
            {
                "intent" or "uncommitted-intent" => intent,
                "dispatch" or "uncommitted-dispatch" => intent.Next(HostTaskState.DispatchRecorded),
                _ => intent.Next(HostTaskState.Succeeded),
            };
            await store.CommitAsync(next, next.Revision.Value - 1, TestContext.Current.CancellationToken);
        }
        await SignalAndWait(root);
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
