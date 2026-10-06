using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteHostTaskStoreTests
{
    [WindowsFact]
    public async Task Cancellation_after_commit_does_not_turn_a_durable_receipt_into_a_cancelled_result()
    {
        using var fixture = new OwnedStorageFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.GetTagItem("kora.storage.boundary") as string,
                    "storage.task.commit", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await new WindowsSqliteHostTaskStore(fixture).CommitAsync(intent, 0, cancellation.Token);
        cancellation.IsCancellationRequested.Should().BeTrue();
        (await new WindowsSqliteHostTaskStore(fixture).ReadTaskAsync(request.TaskId,
            TestContext.Current.CancellationToken)).Should().Be(intent);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Precommit_failure_or_cancellation_rolls_back_both_task_projection_and_ledger(int priorRevision, bool cancel)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        var dispatch = intent.Next(HostTaskState.DispatchRecorded);
        if (priorRevision > 0)
        {
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        }
        if (priorRevision > 1)
        {
            await store.CommitAsync(dispatch, 1, TestContext.Current.CancellationToken);
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var checkpoint = new SqliteTransactionCheckpoint
        {
            Write = SqliteTransactionCheckpoint.SpillPages,
            Commit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT count(*) FROM host_task_events;";
                command.ExecuteScalar().Should().Be(priorRevision + 1L);
                if (cancel)
                {
                    cancellation.Cancel();
                }
                else
                {
                    throw new IOException("Owned fixture precommit failure.");
                }
            },
        };
        var next = priorRevision switch
        {
            0 => intent,
            1 => dispatch,
            _ => dispatch.Next(HostTaskState.Succeeded),
        };
        var failing = new WindowsSqliteHostTaskStore(fixture, checkpoint);
        var commit = () => failing.CommitAsync(next, priorRevision, cancellation.Token).AsTask();
        if (cancel)
        {
            await commit.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await commit.Should().ThrowAsync<IOException>().WithMessage("Owned fixture precommit failure.");
        }
        var reopened = new WindowsSqliteHostTaskStore(fixture);
        var previous = priorRevision switch { 0 => null, 1 => intent, _ => dispatch };
        (await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken)).Should().Be(previous);
        using (var connection = Open(fixture))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM host_task_events;";
            command.ExecuteScalar().Should().Be((long)priorRevision);
        }
        await reopened.CommitAsync(next, priorRevision, TestContext.Current.CancellationToken);
        (await reopened.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken)).Should().Be(next);
    }

    [WindowsFact]
    public async Task Startup_and_bounded_receipt_lookup_need_no_host_context_and_preserve_terminal_receipts()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        (await store.ReadTaskAsync(new(Guid.NewGuid()), TestContext.Current.CancellationToken)).Should().BeNull();
        using var listener = Listen();
        var request = Request();
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        var receipt = intent.Next(HostTaskState.Succeeded);
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
            await store.CommitAsync(receipt, 1, TestContext.Current.CancellationToken);
        }
        (await new WindowsSqliteHostTaskStore(fixture).ReadTaskAsync(request.TaskId,
            TestContext.Current.CancellationToken)).Should().Be(receipt);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var read = () => store.ReadTaskAsync(request.TaskId, cancelled.Token).AsTask();
        await read.Should().ThrowAsync<OperationCanceledException>();
        var invalid = () => store.ReadTaskAsync(default, TestContext.Current.CancellationToken);
        invalid.Should().Throw<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Concurrent_revision_commits_publish_exactly_one_atomic_projection_and_ledger_event()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        var dispatch = intent.Next(HostTaskState.DispatchRecorded);
        var commits = Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await new WindowsSqliteHostTaskStore(fixture).CommitAsync(dispatch, 1, TestContext.Current.CancellationToken);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }).ToArray();
        (await Task.WhenAll(commits)).Count(success => success).Should().Be(1);
        (await store.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken)).Should().Be(dispatch);
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM host_task_events;";
        command.ExecuteScalar().Should().Be(2L);
    }

    [Theory]
    [InlineData("DROP TABLE host_task_events; CREATE TABLE host_task_events(task_id TEXT,revision INTEGER,state INTEGER);")]
    [InlineData("CREATE INDEX unexpected_index ON host_tasks(state);")]
    [InlineData("CREATE TRIGGER hidden_rewrite AFTER INSERT ON host_tasks BEGIN DELETE FROM host_tasks; END;")]
    public async Task Claimed_v1_database_with_changed_semantic_schema_is_rejected_before_mutation(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        Mutate(fixture, mutation);
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var initialize = () => store.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [Theory]
    [InlineData("DELETE FROM host_task_events WHERE revision=1;")]
    [InlineData("UPDATE host_tasks SET request_id='not-an-id';")]
    [InlineData("UPDATE host_task_events SET state=7 WHERE revision=1;")]
    [InlineData("INSERT INTO host_task_events(task_id,revision,state) VALUES('orphan',1,0);")]
    public async Task Integrity_checks_cover_terminal_tasks_complete_ledger_and_orphan_events(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        await store.CommitAsync(intent.Next(HostTaskState.Succeeded), 1, TestContext.Current.CancellationToken);
        Mutate(fixture, mutation);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Actual_private_sqlite_commits_and_reopens_ordered_intent_dispatch_and_receipt()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        var reopened = new WindowsSqliteHostTaskStore(fixture);
        (await reopened.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().Equal(intent);
        var dispatch = intent.Next(HostTaskState.DispatchRecorded);
        await reopened.CommitAsync(dispatch, 1, TestContext.Current.CancellationToken);
        await store.CommitAsync(dispatch.Next(HostTaskState.Succeeded), 2, TestContext.Current.CancellationToken);
        (await reopened.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().BeEmpty();
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM host_task_events;";
        command.ExecuteScalar().Should().Be(3L);
        command.CommandText = "PRAGMA user_version;";
        command.ExecuteScalar().Should().Be(1L);
        command.CommandText = "PRAGMA journal_mode;";
        command.ExecuteScalar().Should().Be("persist");
        command.CommandText = "PRAGMA synchronous;";
        command.ExecuteScalar().Should().Be(2L);
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1", "Keys")).Should().BeFalse();
        host.Complete(HostOperationOutcome.Completed);
    }

    [WindowsFact]
    public async Task Interrupted_intent_and_dispatched_unknown_are_durably_recovered_without_replay()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var store = new WindowsSqliteHostTaskStore(fixture);
        foreach (var dispatched in new[] { false, true })
        {
            var request = Request();
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
            if (dispatched)
            {
                await store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), 1,
                    TestContext.Current.CancellationToken);
            }
            host.Complete(HostOperationOutcome.Unknown);
        }
        var reopened = new WindowsSqliteHostTaskStore(fixture);
        var records = await reopened.ReadIncompleteAsync(10, TestContext.Current.CancellationToken);
        records.Should().HaveCount(2);
        var recovered = new List<HostTaskRecord>();
        foreach (var prior in records)
        {
            using var host = HostActivity.BeginRoot(prior.Request, HostActivityLayer.Application, HostOperation.Recovery);
            var record = prior.Recover();
            await reopened.CommitAsync(record, prior.Revision.Value, TestContext.Current.CancellationToken);
            recovered.Add(record);
            host.Complete(HostOperationOutcome.Completed);
        }
        recovered.Select(record => record.State).Should().BeEquivalentTo(
            [HostTaskState.Interrupted, HostTaskState.Unknown]);
        (await new WindowsSqliteHostTaskStore(fixture).ReadIncompleteAsync(10,
            TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Stale_revision_or_identity_cannot_replace_durable_state()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        var duplicate = () => store.CommitAsync(intent, 0, TestContext.Current.CancellationToken).AsTask();
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        var invalidRevision = () => store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), 0,
            TestContext.Current.CancellationToken).AsTask();
        await invalidRevision.Should().ThrowAsync<InvalidOperationException>();
        var alien = new HostRequest(new(Guid.NewGuid()), request.SessionId, request.TaskId, request.Origin);
        using (var alienHost = HostActivity.BeginRoot(alien, HostActivityLayer.Application, HostOperation.Request))
        {
            var replace = () => store.CommitAsync(new HostTaskRecord(alien, new(2), HostTaskState.DispatchRecorded),
                1, TestContext.Current.CancellationToken).AsTask();
            await replace.Should().ThrowAsync<InvalidOperationException>();
        }
        (await store.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().Equal(intent);
    }

    [WindowsFact]
    public async Task Cancellation_before_dispatch_does_not_create_storage()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var commit = () => store.CommitAsync(new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded),
            0, cancelled.Token).AsTask();
        await commit.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1")).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Missing_or_corrupted_database_never_creates_replacement()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        var path = DatabasePath(fixture);
        await File.WriteAllBytesAsync(path, "Corrupt synthetic database"u8.ToArray(), TestContext.Current.CancellationToken);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
            .Should().Equal("Corrupt synthetic database"u8.ToArray());
        File.Delete(path);
        await read.Should().ThrowAsync<InvalidDataException>();
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("PRAGMA user_version=99;")]
    [InlineData("PRAGMA application_id=0;")]
    public async Task Unknown_schema_or_database_identity_is_preserved_and_rejected(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        Mutate(fixture, mutation);
        var before = await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken)).Should().Equal(before);
    }

    [Theory]
    [InlineData("UPDATE host_tasks SET request_id='invalid';")]
    [InlineData("UPDATE host_tasks SET origin=99;")]
    [InlineData("DELETE FROM host_task_events;")]
    public async Task Invalid_persisted_identity_origin_or_event_projection_is_explicitly_rejected(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.CommitAsync(new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded), 0,
            TestContext.Current.CancellationToken);
        Mutate(fixture, mutation);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Permissive_database_permissions_are_rejected_without_repair()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        var file = new FileInfo(DatabasePath(fixture));
        var acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        var before = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task Database_and_persistent_journal_retain_private_user_ownership_across_commits_and_reopens()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        using var identity = WindowsIdentity.GetCurrent();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        var paths = new[] { DatabasePath(fixture), string.Concat(DatabasePath(fixture), "-journal") };
        var permissions = paths.Select(path => new FileInfo(path).GetAccessControl()
            .GetSecurityDescriptorBinaryForm()).ToArray();
        for (var index = 0; index < 3; index++)
        {
            var request = Request();
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await store.CommitAsync(new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded),
                0, TestContext.Current.CancellationToken);
            (await new WindowsSqliteHostTaskStore(fixture).ReadIncompleteAsync(10,
                TestContext.Current.CancellationToken)).Should().HaveCount(index + 1);
            for (var file = 0; file < paths.Length; file++)
            {
                var security = new FileInfo(paths[file]).GetAccessControl();
                security.GetOwner(typeof(SecurityIdentifier)).Should().Be(identity.User);
                security.AreAccessRulesProtected.Should().BeTrue();
                security.GetSecurityDescriptorBinaryForm().Should().Equal(permissions[file]);
            }
        }
    }

    [WindowsFact]
    public async Task Missing_journal_requires_explicit_recovery_without_recreating_files_or_mutating_the_database()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        var journal = string.Concat(DatabasePath(fixture), "-journal");
        File.Delete(journal);
        var before = await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        File.Exists(journal).Should().BeFalse();
        (await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken))
            .Should().Equal(before);
    }

    [WindowsFact]
    public async Task Permissive_journal_is_rejected_without_repair_or_database_mutation()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        await store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken);
        var journal = new FileInfo(string.Concat(DatabasePath(fixture), "-journal"));
        var acl = journal.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        journal.SetAccessControl(acl);
        var permissions = journal.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var database = await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken);
        var read = () => store.ReadIncompleteAsync(1, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
        journal.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(permissions);
        (await File.ReadAllBytesAsync(DatabasePath(fixture), TestContext.Current.CancellationToken))
            .Should().Equal(database);
    }

    [WindowsFact]
    public void Missing_host_or_invalid_bound_is_rejected_before_io()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsSqliteHostTaskStore(fixture);
        var record = new HostTaskRecord(Request(), new(1), HostTaskState.IntentRecorded);
        var commit = () => store.CommitAsync(record, 0, TestContext.Current.CancellationToken);
        commit.Should().Throw<InvalidOperationException>();
        var read = () => store.ReadIncompleteAsync(101, TestContext.Current.CancellationToken);
        read.Should().Throw<ArgumentOutOfRangeException>();
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1")).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Terminal_state_cannot_be_replaced_and_dispatched_work_cannot_claim_cancelled()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var request = Request();
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var store = new WindowsSqliteHostTaskStore(fixture);
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, TestContext.Current.CancellationToken);
        await store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), 1, TestContext.Current.CancellationToken);
        var falseCancellation = () => store.CommitAsync(new HostTaskRecord(request, new(3), HostTaskState.Cancelled),
            2, TestContext.Current.CancellationToken).AsTask();
        await falseCancellation.Should().ThrowAsync<InvalidOperationException>();
        await store.CommitAsync(new HostTaskRecord(request, new(3), HostTaskState.Unknown),
            2, TestContext.Current.CancellationToken);
        var replace = () => store.CommitAsync(new HostTaskRecord(request, new(4), HostTaskState.Succeeded),
            3, TestContext.Current.CancellationToken).AsTask();
        await replace.Should().ThrowAsync<InvalidOperationException>();
        (await store.ReadIncompleteAsync(10, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Recovery_reads_are_bounded_and_exclusive_storage_ownership_is_required()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var store = new WindowsSqliteHostTaskStore(fixture);
        for (var index = 0; index < 3; index++)
        {
            var request = Request();
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await store.CommitAsync(new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded),
                0, TestContext.Current.CancellationToken);
        }
        (await store.ReadIncompleteAsync(2, TestContext.Current.CancellationToken)).Should().HaveCount(2);
        using var lease = new RestrictedStorageDirectory(fixture, includeKeys: false).AcquireLease();
        var read = () => new WindowsSqliteHostTaskStore(fixture)
            .ReadIncompleteAsync(2, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<IOException>();
    }

    private static HostRequest Request() => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
        RequestOrigin.LocalUi, new(Guid.NewGuid()));

    private static string DatabasePath(OwnedStorageFixture fixture) =>
        Path.Combine(fixture.LocalRoot, "HostStorageV1", "host.db");

    private static SqliteConnection Open(OwnedStorageFixture fixture)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(fixture), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var settings = connection.CreateCommand();
        settings.CommandText = "PRAGMA journal_mode=PERSIST;";
        settings.ExecuteNonQuery();
        return connection;
    }

    private static void Mutate(OwnedStorageFixture fixture, string sql)
    {
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
