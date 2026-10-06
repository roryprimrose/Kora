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
