using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;

using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionRetentionTests
{
    [WindowsFact]
    public async Task V5_migration_preserves_history_queue_and_audit_and_restart_never_replays_held_unknown_work()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var session = fixture.Request.SessionId;
        var entry = await EnqueueAsync(fixture, session);
        var running = await AdmitAsync(fixture, entry);
        var history = await fixture.Store.ReadHistoryAsync(session, null, 50, fixture.Token);
        var audits = fixture.Count("security_audit_events");
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; DROP TABLE session_retention; PRAGMA user_version=5;");
        fixture.Time.Now = fixture.Time.Now.AddYears(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Count("security_audit_events").Should().Be(audits);
        (await fixture.Store.ReadHistoryAsync(session, null, 50, fixture.Token)).Should().BeEquivalentTo(history);
        (await fixture.Store.ReadQueueEntryAsync(session, entry.Request.TaskId, fixture.Token))!.State.Should().Be(SessionQueueState.Unknown);
        var clock = await fixture.Store.ReadRetentionAsync(session, fixture.Token);
        clock.LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(100, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        (await RetainAsync(fixture)).Held.Should().Be(1);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().BeNull();
        var late = () => CompleteAsync(fixture, running);
        await late.Should().ThrowAsync<Exception>();
        (await fixture.Store.ReadRetentionAsync(session, fixture.Token)).Should().Be(clock);
    }

    [WindowsFact]
    public async Task Pending_queue_holds_retention_but_cancellation_and_passive_reads_never_renew_activity()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var session = fixture.Request.SessionId;
        fixture.Time.Now = fixture.Time.Now.AddHours(2);
        var pending = await EnqueueAsync(fixture, session);
        var clock = await fixture.Store.ReadRetentionAsync(session, fixture.Token);
        clock.LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        fixture.Time.Now = clock.DeleteDue;
        (await RetainAsync(fixture)).Held.Should().Be(1);
        var snapshot = await fixture.Store.ReadQueueAsync(session, fixture.Token);
        await QueueControlAsync(fixture, session, control => fixture.Store.RemovePendingAsync(control,
            pending.Generation, snapshot.Revision, pending.Request.TaskId, pending.Revision,
            SessionQueueState.Cancelled, () => true, fixture.Token));
        (await fixture.Store.ReadRetentionAsync(session, fixture.Token)).Should().Be(clock);
        (await RetainAsync(fixture)).Deleted.Should().Be(1);
        fixture.Count("session_queue").Should().Be(0);
        var removed = () => fixture.Store.ReadWorkAsync(session, 1, new(), fixture.Token).AsTask();
        await removed.Should().ThrowAsync<InvalidOperationException>();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadRetentionAsync(session, fixture.Token)).Purged.Should().BeTrue();
    }

    [WindowsFact]
    public async Task Queue_progress_renews_activity_and_inventoried_deletion_preserves_unrelated_pending_work_and_perpetual_audit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var perpetual = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var session = fixture.Request.SessionId;
        var pending = await EnqueueAsync(fixture, session);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        var running = await AdmitAsync(fixture, pending);
        (await fixture.Store.ReadRetentionAsync(session, fixture.Token)).LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        await CompleteAsync(fixture, running);
        var clock = await fixture.Store.ReadRetentionAsync(session, fixture.Token);
        clock.LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(fixture, new())
            .CreateAsync(new("Independent queue"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var independent = await EnqueueAsync(fixture, other);
        var independentWork = await fixture.Store.ReadWorkAsync(other, 1, new(), fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        (await RetainAsync(fixture)).Deleted.Should().Be(1);
        fixture.Count("session_queue").Should().Be(1);
        (await fixture.Store.ReadQueueEntryAsync(other, independent.Request.TaskId, fixture.Token))!.Request.Should().Be(independent.Request);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(perpetual);
        var retainedWork = await fixture.Store.ReadWorkAsync(other, 1, new(), fixture.Token);
        retainedWork.PendingCount.Should().Be(independentWork.PendingCount);
        retainedWork.QueueRecords.Single().Entry.Request.Should().Be(independent.Request);
        (await fixture.Store.ReadHistoryAsync(session, null, 50, fixture.Token)).Records
            .Should().OnlyContain(value => value.Availability == SessionHistoryAvailability.Redacted);
        var late = () => CompleteAsync(fixture, running);
        await late.Should().ThrowAsync<Exception>();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(100, fixture.Token);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().BeNull();
        (await fixture.Store.ReadRetentionAsync(session, fixture.Token)).Purged.Should().BeTrue();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(perpetual);
    }

    [WindowsFact]
    public async Task Current_run_control_holds_cannot_starve_due_ordinary_content_out_of_bounded_batch()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var controls = new List<HostId<SessionIdentity>>();
        for (var i = 0; i <= WindowsSqliteHostInteractionStore.MaximumRetentionSessions; i++)
        {
            fixture.Request = InteractionStorageFixture.NewRequest();
            await fixture.RunAsync(() => fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
            await fixture.RunAsync(() => fixture.Store.CreateDiagnosticRetentionSessionAsync(fixture.Request, static () => true, fixture.Token));
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            controls.Add(fixture.Request.SessionId);
        }
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var ordinary = fixture.Request.SessionId;
        fixture.Time.Now = (await fixture.Store.ReadRetentionAsync(ordinary, fixture.Token)).DeleteDue;
        var result = await RetainAsync(fixture);
        result.Deleted.Should().Be(2);
        result.Held.Should().Be(WindowsSqliteHostInteractionStore.MaximumRetentionSessions - 2);
        result.HasMore.Should().BeTrue();
        (await fixture.Store.ReadRetentionAsync(ordinary, fixture.Token)).Purged.Should().BeTrue();
        foreach (var id in controls)
        {
            (await fixture.Store.ReadSessionAsync(id, fixture.Token))!.IsActive.Should().BeTrue();
        }
    }

    [WindowsFact]
    public async Task Archive_and_delete_share_clock_browsing_does_not_refresh_and_archive_preserves_content()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        clock.ArchiveDue.Should().Be(clock.LastMeaningfulActivity.AddHours(24));
        clock.DeleteDue.Should().Be(clock.LastMeaningfulActivity.AddDays(30));
        fixture.Time.Now = clock.ArchiveDue.AddTicks(-1);
        (await RetainAsync(fixture)).Archived.Should().Be(0);
        await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        var before = await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token);
        fixture.Time.Now = clock.ArchiveDue;
        (await RetainAsync(fixture)).Archived.Should().Be(1);
        (await fixture.Store.ReadSessionAsync(id, fixture.Token))!.IsActive.Should().BeFalse();
        (await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token)).Records.Should().Equal(before.Records);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        fixture.Time.Now = clock.DeleteDue;
        (await RetainAsync(fixture)).Deleted.Should().Be(1);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Purged.Should().BeTrue();
        (await RetainAsync(fixture)).Deleted.Should().Be(0);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token)).Disposed.Should().BeTrue();
    }

    [WindowsFact]
    public async Task Deletion_removes_real_history_metadata_artifacts_staging_and_journal_bytes_preserving_unrelated_and_perpetual()
    {
        const string sentinel = "RETENTION_PRIVATE_CONTENT_9274";
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var perpetual = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await workspace.RenameAsync(target, new(1), 0, new(sentinel), RequestOrigin.LocalUi, fixture.Token);
        fixture.Request = InteractionStorageFixture.NewRequest(target);
        await fixture.AdmitAsync(newSession: false);
        var question = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new(sentinel, QuestionKind.Text, [], 1, 1, 500, "session-retention-proof", "builtin"),
            fixture.Time.Now.AddMinutes(5), fixture.Token));
        await fixture.RunAsync(() => fixture.Questions.SubmitAsync(question.Question!.Key, new([], sentinel),
            RequestOrigin.LocalUi, fixture.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var due = (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).DeleteDue;
        fixture.Time.Now = due.AddHours(-1);
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var unrelated = fixture.Request.SessionId;
        var unrelatedHistory = await fixture.Store.ReadHistoryAsync(unrelated, null, 50, fixture.Token);
        using var key = await new WindowsStorageKeyStore(fixture.Paths).CreateNewAsync(fixture.Token);
        var artifacts = new WindowsEncryptedArtifactStore(fixture.Paths, key);
        var owned = await artifacts.PublishAsync(new(target, Guid.NewGuid(), target, ArtifactRole.SessionAttachment),
            Encoding.UTF8.GetBytes(sentinel), fixture.Token);
        var independent = await artifacts.PublishAsync(new(target, Guid.NewGuid(), unrelated, ArtifactRole.SessionSummary),
            Encoding.UTF8.GetBytes("independent"), fixture.Token);
        var stagedId = Guid.NewGuid();
        var staging = Path.Combine(fixture.Paths.LocalRoot, RestrictedStorageDirectory.PartitionName, "Artifacts", stagedId.ToString("N") + ".pending");
        await StorageFilePublication.WriteStagingAsync(new(fixture.Paths), staging,
            ArtifactEnvelope.Encrypt(key, new(target, stagedId, target, ArtifactRole.ToolResult), Encoding.UTF8.GetBytes(sentinel)),
            StoragePublicationKind.Artifact, checkpoint: null, fixture.Token);
        fixture.Time.Now = due;
        var revoked = new List<HostId<SessionIdentity>>();
        (await RetainAsync(fixture, revoke: (id, _) => { revoked.Add(id); return ValueTask.CompletedTask; })).Deleted.Should().Be(1);
        revoked.Should().Equal(target);
        File.Exists(staging).Should().BeFalse();
        File.Exists(Path.Combine(Path.GetDirectoryName(staging)!, owned.Identity.ArtifactId.ToString("N") + ".gcm")).Should().BeFalse();
        Encoding.UTF8.GetString(await artifacts.ReadAsync(independent, fixture.Token)).Should().Be("independent");
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(perpetual);
        (await fixture.Store.ReadHistoryAsync(unrelated, null, 50, fixture.Token)).Records.Should().Equal(unrelatedHistory.Records);
        foreach (var file in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, fixture.Token)).Should().NotContain(sentinel);
        }
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
        fixture.Count("host_tasks").Should().Be(1);
        fixture.Count("session_metadata").Should().Be(0);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Purged.Should().BeTrue();
        var lateArtifact = () => artifacts.PublishAsync(new(target, Guid.NewGuid(), target, ArtifactRole.SessionAttachment),
            Encoding.UTF8.GetBytes(sentinel), fixture.Token);
        await lateArtifact.Should().ThrowAsync<InvalidOperationException>();
        fixture.Request = InteractionStorageFixture.NewRequest(target);
        var late = () => fixture.RunAsync(() => fixture.Tasks.CommitAsync(new(fixture.Request, new(1), HostTaskState.IntentRecorded),
            0, fixture.Token).AsTask());
        await late.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Unknown)]
    public async Task Live_and_uncertain_work_are_held_not_archived_or_abandoned(HostTaskState state)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        if (state != HostTaskState.IntentRecorded)
        {
            await fixture.RunAsync(() => fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.DispatchRecorded), 1, fixture.Token).AsTask());
            if (state == HostTaskState.Unknown)
            {
                await fixture.RunAsync(() => fixture.Tasks.CommitAsync(new(fixture.Request, new(3), HostTaskState.Unknown), 2, fixture.Token).AsTask());
            }
        }
        var clock = await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
        fixture.Time.Now = clock.DeleteDue.AddDays(1);
        var history = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 25, fixture.Token);
        var result = await RetainAsync(fixture);
        result.Should().Be(new SessionRetentionBatch(0, 0, 1, false));
        (await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 25, fixture.Token)).Records.Should().Equal(history.Records);
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock);
    }

    [WindowsFact]
    public async Task Independent_Perpetual_session_exemption_survives_restart_and_does_not_refresh_activity()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var id = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        await fixture.RunAsync(() => fixture.Store.SetPerpetualAsync(fixture.Request, new(1), true, () => true, fixture.Token).AsTask());
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        fixture.Time.Now = clock.DeleteDue.AddDays(365);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(0, 0, 0, false));
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock with { Perpetual = true });
        fixture.Mutate("UPDATE session_retention SET perpetual=0;");
        var tampered = () => fixture.Store.ReadRetentionAsync(id, fixture.Token).AsTask();
        await tampered.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Cancellation_before_commit_and_lost_admission_preserve_content_and_clock()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        using var cancellation = new CancellationTokenSource();
        var history = await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token);
        var action = () => RetainAsync(fixture, cancellation.Token, (_, _) =>
        {
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        });
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        (await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token)).Records.Should().Equal(history.Records);
        var denied = () => RetainAsync(fixture, eligible: () => false);
        await denied.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("UPDATE session_retention SET last_activity='bad';")]
    [InlineData("UPDATE session_retention SET last_activity='9999-12-31T00:00:00.0000000+00:00';")]
    [InlineData("DELETE FROM session_retention;")]
    public async Task Invalid_persisted_clock_is_explicit_not_defaulted(string corruption)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Mutate(corruption);
        var action = () => fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token).AsTask();
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Future_policy_does_not_rewrite_due_dates_and_resume_uses_monotonic_shared_clock()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        var old = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        var policy = new SessionRetentionPolicy();
        policy.Activate(new(2, 60));
        var store = new WindowsSqliteHostInteractionStore(fixture.Paths, fixture.Tasks, fixture.Time, sessionRetentionPolicy: policy);
        await store.InitializeAsync(fixture.Token);
        (await store.ReadRetentionAsync(id, fixture.Token)).Should().Be(old);
        var workspace = new Kora.Application.Hosting.SessionWorkspaceService(store, new(fixture.Tasks),
            new WindowsSqliteSessionWorkspaceTests.Access(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance);
        await workspace.ChangeLifecycleAsync(id, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        fixture.Time.Now = old.LastMeaningfulActivity.AddDays(-1);
        await workspace.ChangeLifecycleAsync(id, new(2), true, RequestOrigin.LocalUi, fixture.Token);
        var resumed = await store.ReadRetentionAsync(id, fixture.Token);
        resumed.LastMeaningfulActivity.Should().Be(old.LastMeaningfulActivity);
        resumed.ArchiveDue.Should().Be(old.LastMeaningfulActivity.AddDays(2));
        resumed.DeleteDue.Should().Be(old.LastMeaningfulActivity.AddDays(60));
    }

    [WindowsFact]
    public async Task Accepted_input_final_answer_and_progress_touch_one_clock_but_drafts_and_rename_do_not()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        var initial = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        fixture.Time.Now = initial.LastMeaningfulActivity.AddHours(1);
        await WindowsSqliteSessionWorkspaceTests.Service(fixture, new()).RenameAsync(id, new(1), 0,
            new("Passive label edit"), RequestOrigin.LocalUi, fixture.Token);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(initial);
        fixture.Request = InteractionStorageFixture.NewRequest(id);
        await fixture.AdmitAsync(newSession: false);
        var admitted = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        admitted.LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        var question = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Bounded answer", QuestionKind.Text, [], maximumTextLength: 100), fixture.Time.Now.AddHours(1), fixture.Token));
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        var draft = await fixture.RunAsync(() => fixture.Questions.DraftAsync(question.Question!.Key,
            new([], "draft"), RequestOrigin.LocalUi, fixture.Token));
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(admitted);
        await fixture.RunAsync(() => fixture.Questions.SubmitAsync(draft.Question!.Key,
            new([], "answer"), RequestOrigin.LocalUi, fixture.Token));
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).LastMeaningfulActivity.Should().Be(fixture.Time.Now);
    }

    [WindowsFact]
    public async Task Cancellation_at_authority_commit_rolls_back_and_incomplete_acceptance_is_retryable_without_execution()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        using var cancellation = new CancellationTokenSource();
        checkpoint.Commit = (_, _) => cancellation.Cancel();
        var cancelled = () => RetainAsync(fixture, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Should().Be(clock);
        var commits = 0;
        checkpoint.Commit = (_, _) => { if (++commits == 2) { throw new IOException("Acceptance interrupted"); } };
        var interrupted = () => RetainAsync(fixture);
        await interrupted.Should().ThrowAsync<IOException>();
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Purged.Should().BeFalse();
        (await fixture.Store.ReadHistoryAsync(id, null, 25, fixture.Token)).Disposed.Should().BeTrue();
        checkpoint.Commit = null;
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await RetainAsync(fixture)).Deleted.Should().Be(1);
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Purged.Should().BeTrue();
        (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Uninventoried_backup_holds_cleanup_and_v5_migration_uses_conservative_nonexecuting_baseline()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; DROP TABLE session_retention; PRAGMA user_version=5;");
        fixture.Time.Now = fixture.Time.Now.AddYears(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var clock = await fixture.Store.ReadRetentionAsync(id, fixture.Token);
        clock.LastMeaningfulActivity.Should().Be(fixture.Time.Now);
        (await RetainAsync(fixture)).Deleted.Should().Be(0);
        var directory = new RestrictedStorageDirectory(fixture.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
        var backup = Path.Combine(directory.Artifacts, "uninventoried.backup");
        await StorageFilePublication.WriteStagingAsync(directory, backup, Encoding.UTF8.GetBytes("Unknown ownership"),
            StoragePublicationKind.Artifact, checkpoint: null, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        var action = () => RetainAsync(fixture);
        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(backup).Should().BeTrue();
        (await fixture.Store.ReadRetentionAsync(id, fixture.Token)).Purged.Should().BeFalse();
    }

    private static async Task<T> QueueControlAsync<T>(InteractionStorageFixture fixture, HostId<SessionIdentity> session,
        Func<HostRequest, ValueTask<T>> mutate)
    {
        var control = InteractionStorageFixture.NewRequest(session);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        var intent = await fixture.Store.RecordControlIntentAsync(control, fixture.Token);
        var result = await mutate(control);
        await new HostTaskCoordinator(fixture.Tasks).RecordOutcomeAsync(intent, HostTaskState.Succeeded, fixture.Token);
        return result;
    }

    private static async Task<SessionQueueEntry> EnqueueAsync(InteractionStorageFixture fixture, HostId<SessionIdentity> session)
    {
        var snapshot = await fixture.Store.ReadQueueAsync(session, fixture.Token);
        var work = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var next = await QueueControlAsync(fixture, session, control => fixture.Store.EnqueueAsync(control, work,
            snapshot.Generation, snapshot.Revision, 1, null, new(), () => true, fixture.Token));
        return next.Entries.Single(entry => entry.Request.TaskId == work.TaskId);
    }

    private static async Task<SessionQueueEntry> AdmitAsync(InteractionStorageFixture fixture, SessionQueueEntry entry)
    {
        using var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Request);
        return (await fixture.Store.AdmitAsync(entry, 1, new(), () => true, fixture.Token)).Entry;
    }

    private static async Task<SessionQueueEntry> CompleteAsync(InteractionStorageFixture fixture, SessionQueueEntry entry)
    {
        using var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Tool);
        return await fixture.Store.CompleteAsync(entry, SessionQueueState.Succeeded, () => true, fixture.Token);
    }

    private static async Task<SessionRetentionBatch> RetainAsync(InteractionStorageFixture fixture, CancellationToken? token = null,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask>? revoke = null, Func<bool>? eligible = null)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Retention);
        return await fixture.Store.ApplyRetentionAsync(eligible ?? (() => true),
            revoke ?? (static (_, _) => ValueTask.CompletedTask), token ?? fixture.Token);
    }
}
