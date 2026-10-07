using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionLifecycleInterruptionTests
{
    private const string RootVariable = "KORA_SESSION_CHILD_ROOT";
    private const string ModeVariable = "KORA_SESSION_CHILD_MODE";
    private const string Marker = "session-child-ready";

    [Theory]
    [InlineData("create-before")]
    [InlineData("create-after")]
    [InlineData("rename-before")]
    [InlineData("rename-after")]
    [InlineData("migrate-before")]
    [InlineData("migrate-after")]
    [InlineData("consolidate-before")]
    [InlineData("consolidate-after")]
    public async Task Owned_metadata_or_migration_kill_reopens_one_atomic_schema_authority_and_audit_truth(string mode)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var consolidation = mode.StartsWith("consolidate-", StringComparison.Ordinal);
        var migration = consolidation || mode.StartsWith("migrate-", StringComparison.Ordinal);
        if (migration) { fixture.StageLegacy(consolidation ? 2 : 1); }
        var audits = fixture.Count("security_audit_events");
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteSessionLifecycleInterruptionTests),
            nameof(Fixture_owned_child_metadata_only));
        start.Environment[RootVariable] = fixture.Paths.LocalRoot;
        start.Environment[ModeVariable] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned metadata child did not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!File.Exists(Path.Combine(fixture.Paths.LocalRoot, Marker)))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"Owned metadata child failed before checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            var committed = mode.EndsWith("-after", StringComparison.Ordinal);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.Paths.LocalRoot, fixture.DatabasePath, deadline.Token);
            if (migration)
            {
                using var connection = fixture.OpenRaw();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                ((long)command.ExecuteScalar()!).Should().Be(committed ? 3 : consolidation ? 2 : 1);
            }
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            var rows = (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records;
            rows.Length.Should().Be(mode.StartsWith("create-", StringComparison.Ordinal) && committed ? 2 : 1);
            rows.Count(row => row.Metadata is not null).Should().Be(!migration && committed ? 1 : 0);
            rows.Should().OnlyContain(row => row.Authority.Generation.Value == 1 && row.Authority.IsActive);
            fixture.Count("security_audit_events").Should().Be(audits + (!migration && committed ? 1 : 0));
            if (!migration)
            {
                var recovered = await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token);
                recovered.Should().ContainSingle().Which.State.Should().Be(HostTaskState.Interrupted);
                (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Should().Equal(rows);
            }
            else { (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().BeEmpty(); }
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Fixture_owned_child_metadata_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null) { return; }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("create-before" or "create-after" or "rename-before" or "rename-after" or "migrate-before" or "migrate-after"
            or "consolidate-before" or "consolidate-after"))
        {
            throw new InvalidOperationException("Invalid owned metadata child mode.");
        }
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var paths = new ExistingPaths(root);
        var tasks = new WindowsSqliteHostTaskStore(paths);
        var checkpoint = new InteractionTransactionCheckpoint();
        var store = new WindowsSqliteHostInteractionStore(paths, tasks, new InteractionStorageFixture.Clock(), checkpoint);
        if (mode.EndsWith("-before", StringComparison.Ordinal))
        {
            checkpoint.Commit = (_, _) => OwnedStorageChildProcess.SignalAndBlock(root, Marker);
        }
        var token = TestContext.Current.CancellationToken;
        if (mode.StartsWith("migrate-", StringComparison.Ordinal) || mode.StartsWith("consolidate-", StringComparison.Ordinal))
        { await store.InitializeAsync(token); }
        else
        {
            var session = (await store.ReadSessionsAsync(null, 1, token)).Records.Single();
            var request = new HostRequest(new(Guid.NewGuid()),
                mode.StartsWith("create-", StringComparison.Ordinal) ? new(Guid.NewGuid()) : session.SessionId,
                new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await store.RecordControlIntentAsync(request, token);
            if (mode.StartsWith("create-", StringComparison.Ordinal))
            {
                await store.CreateNamedSessionAsync(request, new("Owned metadata"), () => true, token);
            }
            else { await store.RenameSessionAsync(request, session.Generation, 0, new("Owned metadata"), () => true, token); }
        }
        OwnedStorageChildProcess.SignalAndBlock(root, Marker);
    }

    [Theory]
    [InlineData("done-before")]
    [InlineData("done-after")]
    [InlineData("resume-before")]
    [InlineData("resume-after")]
    public async Task Owned_process_kill_at_guarded_lifecycle_commit_reopens_exact_generation_and_audit_without_replay(string mode)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var resume = mode.StartsWith("resume-", StringComparison.Ordinal);
        if (resume)
        {
            await WindowsSqliteSessionWorkspaceTests.Service(fixture, new()).ChangeLifecycleAsync(
                fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        }
        var generation = resume ? 2 : 1;
        var audits = fixture.Count("security_audit_events");
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteSessionLifecycleInterruptionTests),
            nameof(Fixture_owned_child_lifecycle_only));
        start.Environment[RootVariable] = fixture.Paths.LocalRoot;
        start.Environment[ModeVariable] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned lifecycle child did not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!File.Exists(Path.Combine(fixture.Paths.LocalRoot, Marker)))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"Owned lifecycle child failed before checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            var committed = mode.EndsWith("-after", StringComparison.Ordinal);
            if (!committed) { OwnedStorageChildProcess.AssertHotJournal(fixture.DatabasePath + "-journal"); }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.Paths.LocalRoot, fixture.DatabasePath, deadline.Token);
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            var session = (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!;
            session.Generation.Value.Should().Be(generation + (committed ? 1 : 0));
            session.IsActive.Should().Be(committed ? resume : !resume);
            fixture.Count("security_audit_events").Should().Be(audits + (committed ? 1 : 0));
            var control = (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().ContainSingle().Which;
            control.State.Should().Be(HostTaskState.IntentRecorded);
            var recovered = await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token);
            recovered.Should().ContainSingle().Which.State.Should().Be(HostTaskState.Interrupted);
            (await fixture.Store.ReadSessionAsync(session.SessionId, fixture.Token)).Should().Be(session);
            fixture.Count("security_audit_events").Should().Be(audits + (committed ? 1 : 0));
            (await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token)).Should().BeEmpty();
            (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.Succeeded);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Fixture_owned_child_lifecycle_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null) { return; }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("done-before" or "done-after" or "resume-before" or "resume-after"))
        {
            throw new InvalidOperationException("Invalid owned lifecycle child mode.");
        }
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var paths = new ExistingPaths(root);
        var tasks = new WindowsSqliteHostTaskStore(paths);
        var checkpoint = new InteractionTransactionCheckpoint();
        var store = new WindowsSqliteHostInteractionStore(paths, tasks, new InteractionStorageFixture.Clock(), checkpoint);
        var token = TestContext.Current.CancellationToken;
        var session = (await store.ReadSessionsAsync(null, 1, token)).Records.Single();
        var request = new HostRequest(new(Guid.NewGuid()), session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, token);
        if (mode.EndsWith("-before", StringComparison.Ordinal))
        {
            checkpoint.Audit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "PRAGMA cache_size=1; PRAGMA cache_spill=ON;";
                command.ExecuteNonQuery();
            };
            checkpoint.Commit = (_, _) => OwnedStorageChildProcess.SignalAndBlock(root, Marker);
        }
        var changed = await store.ChangeIdleLifecycleAsync(request, session.Generation,
            mode.StartsWith("resume-", StringComparison.Ordinal), () => true, token);
        changed.Generation.Value.Should().Be(session.Generation.Value + 1);
        OwnedStorageChildProcess.SignalAndBlock(root, Marker);
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => Path.Combine(root, "UnusedRoaming");
    }
}
