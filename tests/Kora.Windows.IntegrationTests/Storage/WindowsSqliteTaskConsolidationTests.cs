using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteTaskConsolidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Complete_legacy_migration_preserves_ids_events_questions_generations_and_independent_grants(int version)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var intent = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        var grant = await f.GrantAsync("perpetual");
        var questions = await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var dispatched = await f.RunAsync(() => new Kora.Application.Hosting.HostTaskCoordinator(f.Tasks).RecordDispatchAsync(
            new(f.Request, new(1), HostTaskState.IntentRecorded), f.Token));
        var audits = f.Count("security_audit_events");
        f.StageLegacy(version);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Tasks.ReadTaskAsync(intent.Request.TaskId, f.Token)).Should().Be(intent);
        (await f.Tasks.ReadTaskAsync(dispatched.Request.TaskId, f.Token)).Should().Be(dispatched);
        (await f.Store.ReadQuestionsAsync(intent.Request.SessionId, f.Token)).Should().BeEquivalentTo(questions);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
        (await f.Store.ReadSessionAsync(intent.Request.SessionId, f.Token))!.Generation.Value.Should().Be(1);
        f.Count("security_audit_events").Should().Be(audits);
        using var legacy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(f.Paths.LocalRoot, "HostStorageV1", "host.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        legacy.Open();
        using var command = legacy.CreateCommand();
        command.CommandText = "SELECT frozen FROM task_authority_handoff;";
        command.ExecuteScalar().Should().Be(1L);
        var stale = new WindowsSqliteHostTaskStore(f.Paths);
        var readOld = () => stale.ReadIncompleteAsync(10, f.Token).AsTask();
        await readOld.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task Interrupted_consolidation_keeps_complete_old_schema_freezes_source_and_recovers_only_storage_not_work()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var original = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        var audits = f.Count("security_audit_events");
        f.StageLegacy(2);
        var checkpoint = new InteractionTransactionCheckpoint { Commit = (_, _) => throw new IOException("Storage interruption") };
        f.Reopen(checkpoint);
        var migrate = () => f.Store.InitializeAsync(f.Token).AsTask();
        await migrate.Should().ThrowAsync<IOException>();
        using (var connection = f.OpenRaw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            command.ExecuteScalar().Should().Be(2L);
            command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='host_tasks';";
            command.ExecuteScalar().Should().Be(0L);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(original);
        f.Count("security_audit_events").Should().Be(audits);
    }

    [WindowsFact]
    public async Task Missing_retired_handoff_is_never_initialized_as_a_new_empty_ledger()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var partition = Path.Combine(f.Paths.LocalRoot, "HostStorageV1");
        Directory.Move(partition, Path.Combine(f.Paths.LocalRoot, "OwnedSavedTaskReceipt"));
        var newTasks = new WindowsSqliteHostTaskStore(f.Paths);
        var initialize = () => newTasks.InitializeAsync(f.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(partition).Should().BeFalse();
        f.Reopen();
        var reopen = () => f.Store.InitializeAsync(f.Token).AsTask();
        await reopen.Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(partition).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Unfrozen_handoff_or_missing_question_task_binding_blocks_complete_migration_without_partial_import()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await f.PresentAsync();
        var sourcePath = Path.Combine(f.Paths.LocalRoot, "HostStorageV1", "host.db");
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            source.Open();
            using var command = source.CreateCommand();
            command.CommandText = "UPDATE task_authority_handoff SET frozen=0;";
            command.ExecuteNonQuery();
        }
        f.Reopen();
        var unfrozen = () => f.Store.InitializeAsync(f.Token).AsTask();
        await unfrozen.Should().ThrowAsync<InvalidDataException>();
        f.StageLegacy(2);
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            source.Open();
            using var command = source.CreateCommand();
            command.CommandText = "DELETE FROM host_task_events; DELETE FROM host_tasks;";
            command.ExecuteNonQuery();
        }
        f.Reopen();
        var invalid = () => f.Store.InitializeAsync(f.Token).AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
        using var target = f.OpenRaw();
        using var verify = target.CreateCommand();
        verify.CommandText = "PRAGMA user_version;";
        verify.ExecuteScalar().Should().Be(2L);
        verify.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='host_tasks';";
        verify.ExecuteScalar().Should().Be(0L);
        verify.CommandText = "SELECT question_id FROM host_questions;";
        verify.ExecuteScalar().Should().Be(question.Key.QuestionId.Value.ToString("D"));
    }

    [WindowsFact]
    public async Task Lost_consolidated_authority_is_not_reconstructed_from_retired_source()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var partition = Path.Combine(f.Paths.LocalRoot, HostInteractionSchema.Partition);
        var saved = Path.Combine(f.Paths.LocalRoot, "OwnedSavedAuthority");
        Directory.Move(partition, saved);
        f.Reopen();
        var initialize = () => f.Store.InitializeAsync(f.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(partition).Should().BeFalse();
        var read = () => f.Tasks.ReadIncompleteAsync(10, f.Token).AsTask();
        await read.Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(partition).Should().BeFalse();
    }
}
