using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionMetadataTests
{
    [WindowsFact]
    public async Task Named_creation_reopens_empty_active_authority_and_duplicate_names_never_resolve_subjects()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var audits = fixture.Count("security_audit_events");
        var name = new SessionName("Private café \U0001F600");
        var first = await service.CreateAsync(name, RequestOrigin.LocalUi, fixture.Token);
        var second = await service.CreateAsync(name, RequestOrigin.ActivatedVoice, fixture.Token);
        first.Authority.SessionId.Should().NotBe(second.Authority.SessionId);
        first.Authority.Generation.Value.Should().Be(1);
        first.Metadata!.Revision.Value.Should().Be(1);
        first.Authority.IsActive.Should().BeTrue();
        (await fixture.Store.ReadQuestionsAsync(first.Authority.SessionId, fixture.Token)).Should().BeEmpty();
        (await fixture.Store.ReadTaskPageAsync(first.Authority.SessionId, null, 25, fixture.Token)).Records
            .Should().ContainSingle().Which.State.Should().Be(HostTaskState.Succeeded);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
        fixture.Count("security_audit_events").Should().Be(audits + 2);
        fixture.Reopen();
        var rows = new List<Kora.Core.Storage.SessionWorkspaceEntry>();
        Guid? cursor = null;
        do
        {
            var page = await fixture.Store.ReadMetadataPageAsync(cursor, 1, fixture.Token);
            page.Records.Length.Should().BeLessThanOrEqualTo(1);
            rows.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        rows.Should().HaveCount(3);
        rows.Single(row => row.Authority.SessionId == first.Authority.SessionId).Should().Be(first);
        rows.Single(row => row.Authority.SessionId == second.Authority.SessionId).Should().Be(second);
        rows.Single(row => row.Authority.SessionId == fixture.Request.SessionId).Metadata.Should().BeNull();
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) { reader.GetString(0).Should().NotContain(name.Value); }
    }

    [WindowsFact]
    public async Task Rename_preserves_Done_generation_typed_history_and_independent_grants_and_rejects_stale_tokens()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var perpetual = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var done = await service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        var questions = await fixture.Store.ReadQuestionsAsync(done.SessionId, fixture.Token);
        var first = await service.RenameAsync(done.SessionId, done.Generation, 0, new("Done name"), RequestOrigin.LocalUi, fixture.Token);
        first.Authority.Should().Be(done);
        first.Metadata!.Revision.Value.Should().Be(1);
        var stale = () => service.RenameAsync(done.SessionId, done.Generation, 0, new("Stale"), RequestOrigin.LocalUi, fixture.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var wrongGeneration = () => service.RenameAsync(done.SessionId, new(1), 1, new("Stale"), RequestOrigin.LocalUi, fixture.Token);
        await wrongGeneration.Should().ThrowAsync<InvalidOperationException>();
        var second = await service.RenameAsync(done.SessionId, done.Generation, 1, new("Done renamed"), RequestOrigin.ActivatedVoice, fixture.Token);
        second.Authority.Should().Be(done);
        second.Metadata!.Revision.Value.Should().Be(2);
        fixture.Reopen();
        (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Single().Should().Be(second);
        (await fixture.Store.ReadQuestionsAsync(done.SessionId, fixture.Token)).Should().BeEquivalentTo(questions, options => options.WithStrictOrdering());
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        var absent = () => service.RenameAsync(new(Guid.NewGuid()), new(1), 0, new("Missing"), RequestOrigin.LocalUi, fixture.Token);
        await absent.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task V1_migration_preserves_exact_authority_audit_questions_grants_and_tasks_without_invented_names()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("session");
        var authority = await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token);
        var questions = await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token);
        var audits = fixture.Count("security_audit_events");
        fixture.StageLegacy(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var row = (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Single();
        row.Authority.Should().Be(authority);
        row.Metadata.Should().BeNull();
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Should().BeEquivalentTo(questions, options => options.WithStrictOrdering());
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(grant);
        fixture.Count("security_audit_events").Should().Be(audits);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
        await WindowsSqliteSessionWorkspaceTests.Service(fixture, new()).RenameAsync(row.Authority.SessionId,
            row.Authority.Generation, 0, new("Migrated name"), RequestOrigin.LocalUi, fixture.Token);
        fixture.Reopen();
        (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Single().Metadata!.Name.Value.Should().Be("Migrated name");
    }

    [Theory]
    [InlineData("PRAGMA user_version=99;")]
    [InlineData("PRAGMA user_version=0;")]
    [InlineData("PRAGMA application_id=1;")]
    [InlineData("UPDATE session_metadata SET name='altered';")]
    [InlineData("UPDATE session_metadata SET name='e' || char(769);")]
    [InlineData("DELETE FROM session_metadata;")]
    [InlineData("DROP TABLE session_metadata;")]
    [InlineData("DROP TABLE session_metadata; PRAGMA user_version=1;")]
    public async Task Unknown_corrupt_or_missing_metadata_refuses_without_replacement(string sql)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.Service(fixture, new()).RenameAsync(fixture.Request.SessionId,
            new(1), 0, new("Original"), RequestOrigin.LocalUi, fixture.Token);
        fixture.Mutate(sql);
        fixture.Reopen();
        var read = () => fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Missing_partitions_and_invalid_original_lineage_cannot_create_replacement_authority()
    {
        using var fixture = new InteractionStorageFixture();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var create = () => service.CreateAsync(new("New"), RequestOrigin.LocalUi, fixture.Token);
        await create.Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(Path.Combine(fixture.Paths.LocalRoot, HostInteractionSchema.Partition)).Should().BeFalse();
        await fixture.InitializeAsync();
        var invalid = () => service.CreateAsync(new("New"), RequestOrigin.HostSystem, fixture.Token);
        await invalid.Should().ThrowAsync<InvalidOperationException>();
        var invalidRename = () => service.RenameAsync(fixture.Request.SessionId, new(1), 0,
            new("New"), RequestOrigin.HostSystem, fixture.Token);
        await invalidRename.Should().ThrowAsync<InvalidOperationException>();
        var passive = () => fixture.Store.ReadMetadataPageAsync(Guid.Empty, 51, fixture.Token).AsTask();
        await passive.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Count("session_metadata").Should().Be(0);
        File.Delete(fixture.DatabasePath);
        await create.Should().ThrowAsync<InvalidDataException>();
        File.Exists(fixture.DatabasePath).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Identical_names_do_not_transfer_rename_authority_or_permit_duplicate_identity_creation()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var first = await service.CreateAsync(new("Same"), RequestOrigin.LocalUi, fixture.Token);
        var second = await service.CreateAsync(new("Same"), RequestOrigin.LocalUi, fixture.Token);
        await service.RenameAsync(first.Authority.SessionId, first.Authority.Generation, 1, new("Changed"),
            RequestOrigin.LocalUi, fixture.Token);
        var rows = (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records;
        rows.Single(row => row.Authority.SessionId == second.Authority.SessionId).Should().Be(second);
        var request = InteractionStorageFixture.NewRequest(first.Authority.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await fixture.Store.RecordControlIntentAsync(request, fixture.Token);
        var duplicate = () => fixture.Store.CreateNamedSessionAsync(request, new("Same"), () => true, fixture.Token).AsTask();
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        var negativeRevision = () => fixture.Store.RenameSessionAsync(request, first.Authority.Generation, -1,
            new("Same"), () => true, fixture.Token).AsTask();
        await negativeRevision.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [WindowsFact]
    public async Task Rolling_metadata_back_to_an_older_valid_audit_commit_is_detected()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("First"), RequestOrigin.LocalUi, fixture.Token);
        var firstSequence = fixture.Count("security_audit_events");
        await service.RenameAsync(fixture.Request.SessionId, new(1), 1, new("Second"), RequestOrigin.LocalUi, fixture.Token);
        fixture.Mutate(FormattableString.Invariant($"UPDATE session_metadata SET name='First',revision=1,audit_sequence={firstSequence};"));
        fixture.Reopen();
        var read = () => fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*Committed session metadata is missing*");
    }

    [WindowsFact]
    public async Task Invalid_v1_authority_is_not_migrated_or_reinterpreted_as_unnamed()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Mutate("DROP TABLE session_metadata; PRAGMA user_version=1; UPDATE authority_head SET sequence=0;");
        fixture.Reopen();
        var initialize = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        ((long)command.ExecuteScalar()!).Should().Be(1);
        command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='session_metadata';";
        ((long)command.ExecuteScalar()!).Should().Be(0);
    }

    [WindowsFact]
    public async Task Cancelled_migration_keeps_v1_schema_and_all_authority_for_a_later_valid_reopen()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var audits = fixture.Count("security_audit_events");
        fixture.StageLegacy(1);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var checkpoint = new InteractionTransactionCheckpoint { Commit = (_, _) => cancelled.Cancel() };
        fixture.Reopen(checkpoint);
        var initialize = () => fixture.Store.InitializeAsync(cancelled.Token).AsTask();
        await initialize.Should().ThrowAsync<OperationCanceledException>();
        using (var connection = fixture.OpenRaw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            ((long)command.ExecuteScalar()!).Should().Be(1);
            command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='session_metadata';";
            ((long)command.ExecuteScalar()!).Should().Be(0);
        }
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Count("security_audit_events").Should().Be(audits);
        (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Single().Metadata.Should().BeNull();
    }

    [WindowsFact]
    public async Task Scoped_authority_pending_questions_and_uncertain_work_are_not_changed_by_rename()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("session");
        var pending = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Still pending", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Unknown);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("Only a label"), RequestOrigin.LocalUi, fixture.Token);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(grant);
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single(question => question.Key.Equals(pending.Question!.Key))
            .Status.Should().Be(QuestionStatus.Pending);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.Unknown);
        var done = () => service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await done.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
    }

    [WindowsFact]
    public async Task Stale_competing_renames_have_one_durable_revision_and_names_never_enter_activity_tags()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        async Task<bool> RenameAsync(string name)
        {
            try
            {
                await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new(name), RequestOrigin.LocalUi, fixture.Token);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var outcomes = await Task.WhenAll(RenameAsync("Private first"), RenameAsync("Private second"));
        outcomes.Count(success => success).Should().Be(1);
        fixture.Reopen();
        (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Single().Metadata!.Revision.Value.Should().Be(1);
        stopped.Should().NotBeEmpty();
        stopped.SelectMany(activity => activity.Tags).Should().NotContain(tag =>
            tag.Value != null && tag.Value.Contains("Private", StringComparison.Ordinal));
        stopped.Select(activity => activity.OperationName).Should().NotContain(name => name.Contains("Private", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("create", "audit")]
    [InlineData("rename", "audit")]
    [InlineData("create", "privacy")]
    [InlineData("rename", "privacy")]
    [InlineData("create", "call-revision")]
    [InlineData("rename", "call-revision")]
    [InlineData("create", "cancel")]
    [InlineData("rename", "cancel")]
    public async Task Audit_gate_races_and_cancellation_roll_back_metadata_and_authority_together(string operation, string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        if (failure is "audit") { checkpoint.Audit = (_, _) => throw new IOException("required audit failed"); }
        else
        {
            checkpoint.Commit = (_, _) =>
            {
                if (failure is "privacy") { access.CanControl = false; }
                else if (failure is "call-revision") { access.ControlRevision++; }
                else { cancellation.Cancel(); }
            };
        }
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var audits = fixture.Count("security_audit_events");
        var act = () => operation is "create"
            ? service.CreateAsync(new("Sensitive name"), RequestOrigin.LocalUi, cancellation.Token)
            : service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("Sensitive name"), RequestOrigin.LocalUi, cancellation.Token);
        await act.Should().ThrowAsync<Exception>();
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Count("work_sessions").Should().Be(1);
        fixture.Count("session_metadata").Should().Be(0);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
    }

    [WindowsFact]
    public async Task Native_create_and_exact_selected_rename_clear_name_content_on_privacy_and_do_not_dispatch_work()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var evidence = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
            NullLogger<DurableEvidenceQuery>.Instance);
        var model = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(fixture, access),
            evidence, access, NullLogger<SessionsViewModel>.Instance);
        model.NameDraft = "Native name";
        model.CanCreate.Should().BeTrue();
        model.CanRename.Should().BeFalse();
        await model.CreateAsync();
        model.Status.Should().Contain("empty Active");
        model.Detail.Should().Contain("Native name");
        fixture.Count("work_sessions").Should().Be(2);
        await model.RefreshAsync();
        var target = model.Sessions.Single(row => row.Metadata is not null);
        var before = fixture.Count("security_audit_events");
        await model.SelectAsync(target);
        model.NameDraft.Should().Be("Native name");
        fixture.Count("security_audit_events").Should().Be(before);
        model.NameDraft = "Renamed native";
        await model.RenameAsync();
        model.Detail.Should().Contain("Renamed native").And.Contain("generation 1");
        await model.RenameAsync();
        model.Detail.Should().Contain("Metadata revision: 3");
        model.NameDraft = "a\nb";
        await model.RenameAsync();
        model.Status.Should().Contain("single-line");
        model.NameDraft.Should().BeEmpty();
        model.Sessions.Should().BeEmpty();
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
        access.CanInspect = false;
        await model.RefreshAsync();
        model.NameDraft.Should().BeEmpty();
        model.Detail.Should().BeEmpty();
        model.Close();
    }
}
