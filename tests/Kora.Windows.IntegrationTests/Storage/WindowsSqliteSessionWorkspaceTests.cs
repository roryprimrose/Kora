using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionWorkspaceTests
{
    [WindowsFact]
    public async Task Passive_pages_refuse_missing_partitions_without_creating_empty_authority()
    {
        using var fixture = new InteractionStorageFixture();
        var sessions = () => fixture.Store.ReadSessionsAsync(null, 25, fixture.Token).AsTask();
        var questions = () => fixture.Store.ReadQuestionPageAsync(fixture.Request.SessionId, null, 25, fixture.Token).AsTask();
        var tasks = () => fixture.Store.ReadTaskPageAsync(fixture.Request.SessionId, null, 25, fixture.Token).AsTask();
        await sessions.Should().ThrowAsync<FileNotFoundException>();
        await questions.Should().ThrowAsync<FileNotFoundException>();
        await tasks.Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(Path.Combine(fixture.Paths.LocalRoot, "InteractionStorageV1")).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.Paths.LocalRoot, "HostStorageV1")).Should().BeFalse();
        await fixture.InitializeAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var database = fixture.DatabasePath;
        File.Delete(database);
        await sessions.Should().ThrowAsync<InvalidDataException>().WithMessage("*replacement is forbidden*");
        var control = () => Service(fixture, new()).ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false,
            RequestOrigin.LocalUi, fixture.Token);
        await control.Should().ThrowAsync<InvalidDataException>().WithMessage("*replacement is forbidden*");
        File.Exists(database).Should().BeFalse();
    }

    [WindowsFact]
    public async Task Missing_task_ledger_cannot_be_recreated_and_misclassified_as_idle_by_control_intent()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var ledger = Path.Combine(fixture.Paths.LocalRoot, HostInteractionSchema.Partition);
        Directory.Move(ledger, Path.Combine(fixture.Paths.LocalRoot, "OwnedSavedHostStorageV1"));
        var act = () => Service(fixture, new()).ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false,
            RequestOrigin.LocalUi, fixture.Token);
        await act.Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(ledger).Should().BeFalse();
        Directory.Move(Path.Combine(fixture.Paths.LocalRoot, "OwnedSavedHostStorageV1"), ledger);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
    }
    [WindowsFact]
    public async Task Actual_writer_passive_pages_show_typed_history_without_mutating_or_cross_session_records()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var first = fixture.Request;
        for (var i = 0; i < 3; i++)
        {
            await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
                new("Known question " + i, QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        }
        for (var i = 0; i < 2; i++)
        {
            fixture.Request = InteractionStorageFixture.NewRequest();
            await fixture.AdmitAsync(newSession: true);
        }
        var count = fixture.Count("security_audit_events");
        var sessions = await fixture.Store.ReadSessionsAsync(null, 2, fixture.Token);
        sessions.Records.Should().HaveCount(2);
        sessions.Next.Should().NotBeNull();
        var final = await fixture.Store.ReadSessionsAsync(sessions.Next, 2, fixture.Token);
        final.Records.Should().ContainSingle();
        final.Next.Should().BeNull();
        sessions.Records.Concat(final.Records).Select(s => s.SessionId).Distinct().Should().HaveCount(3);
        var questions = await fixture.Store.ReadQuestionPageAsync(first.SessionId, null, 2, fixture.Token);
        questions.Records.Should().HaveCount(2).And.OnlyContain(q => q.Key.Request.SessionId == first.SessionId);
        var tail = await fixture.Store.ReadQuestionPageAsync(first.SessionId, questions.Next, 2, fixture.Token);
        tail.Records.Should().ContainSingle();
        tail.Next.Should().BeNull();
        (await fixture.Store.ReadTaskPageAsync(first.SessionId, null, 1, fixture.Token)).Records
            .Should().ContainSingle().Which.Request.Should().Be(first);
        fixture.Count("security_audit_events").Should().Be(count);
        (await fixture.Store.ReadSessionAsync(first.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        var invalid = () => fixture.Store.ReadSessionsAsync(Guid.Empty, 51, fixture.Token).AsTask();
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var invalidLimit = () => fixture.Store.ReadQuestionPageAsync(first.SessionId, null, 0, fixture.Token).AsTask();
        await invalidLimit.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [WindowsFact]
    public async Task Real_service_and_native_state_Done_resume_preserve_history_invalidate_scoped_grants_and_do_not_replay()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var scoped = await fixture.GrantAsync("session");
        await FinishAsync(fixture, HostTaskState.Succeeded);
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var perpetual = await fixture.GrantAsync("perpetual");
        var answered = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).First();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new Access();
        var service = Service(fixture, access);
        var evidenceSink = new Kora.Windows.Storage.WindowsSqliteEvidenceSink(fixture.Paths);
        evidenceSink.Initialize();
        var evidence = new DurableEvidenceQuery(new Kora.Windows.Storage.WindowsSqliteEvidenceReader(evidenceSink),
            access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance);
        var state = new SessionsViewModel(service, evidence, access, NullLogger<SessionsViewModel>.Instance);
        await state.RefreshAsync();
        state.Sessions.Should().ContainSingle();
        await state.SelectAsync(state.Sessions[0]);
        state.Detail.Should().Contain(fixture.Request.SessionId.Value.ToString("D")).And.Contain("Succeeded")
            .And.Contain(answered.Spec.Text);
        var audits = fixture.Count("security_audit_events");
        await state.ReadEvidenceAsync();
        state.Detail.Should().Contain("Conversation");
        fixture.Count("security_audit_events").Should().Be(audits);
        state.CanDone.Should().BeTrue();
        await state.ChangeLifecycleAsync(active: false);
        state.Status.Should().StartWith("Committed Done");
        state.Detail.Should().Contain("Done | generation 2");
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(g => g.Id == scoped.Id).Status.Should().Be(OperationGrantStatus.Revoked);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(g => g.Id == perpetual.Id).Should().Be(perpetual);
        fixture.Reopen();
        var done = (await fixture.Store.ReadSessionsAsync(null, 1, fixture.Token)).Records.Single();
        done.IsActive.Should().BeFalse();
        done.Generation.Value.Should().Be(2);
        var restarted = Service(fixture, access);
        var resumed = await restarted.ChangeLifecycleAsync(done.SessionId, done.Generation, true, RequestOrigin.LocalUi, fixture.Token);
        resumed.Generation.Value.Should().Be(3);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(g => g.Id == scoped.Id).Status.Should().Be(OperationGrantStatus.Revoked);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(g => g.Id == perpetual.Id).Should().Be(perpetual);
        var records = await fixture.Store.ReadQuestionsAsync(done.SessionId, fixture.Token);
        records.Should().HaveCount(2).And.OnlyContain(q => q.Status == QuestionStatus.Answered && q.SessionGeneration.Value == 1);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.Succeeded);
        var oldRequest = () => fixture.RunAsync(() => fixture.Store.PublishTrustedSnapshotAsync(fixture.Request, fixture.Policy, fixture.Proposal, 0, fixture.Token));
        await oldRequest.Should().ThrowAsync<InvalidDataException>();
        state.Close();
        state.Detail.Should().BeEmpty();
        state.Sessions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Unknown)]
    public async Task Nonterminal_and_Unknown_tasks_are_authoritative_blockers_not_autoabandoned(HostTaskState state)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        if (state != HostTaskState.IntentRecorded) { await FinishAsync(fixture, state); }
        var before = fixture.Count("security_audit_events");
        var act = () => Service(fixture, new()).ChangeLifecycleAsync(fixture.Request.SessionId,
            new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(state);
        fixture.Count("security_audit_events").Should().Be(before);
    }

    [WindowsFact]
    public async Task Unresolved_question_and_stale_generation_are_rejected_without_closing_records()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var pending = await fixture.PresentAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var service = Service(fixture, new());
        var act = () => service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unresolved*");
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Key.Should().Be(pending.Key);
        // The original task cannot answer after terminal completion. No automatic abandonment
        // or synthetic replacement question is introduced to make this session eligible.
        fixture.Time.Now = fixture.Time.Now.AddDays(1);
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
    }

    [WindowsFact]
    public async Task Revision_races_atomic_audit_failure_and_commit_gate_changes_do_not_claim_success()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new Access();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        var service = Service(fixture, access);
        var audits = fixture.Count("security_audit_events");
        checkpoint.Audit = (_, _) => throw new IOException("required authority audit failed");
        var act = () => service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await act.Should().ThrowAsync<IOException>();
        fixture.Count("security_audit_events").Should().Be(audits);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(100, fixture.Token);
        checkpoint.Audit = null;
        checkpoint.Commit = (_, _) => access.ControlRevision++;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit boundary*");
        fixture.Count("security_audit_events").Should().Be(audits);
        checkpoint.Commit = null;
        var done = await act();
        done.Generation.Value.Should().Be(2);
        var stale = () => service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), true, RequestOrigin.LocalUi, fixture.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*conflicts*");
        (await service.ChangeLifecycleAsync(fixture.Request.SessionId, new(2), true, RequestOrigin.ActivatedVoice, fixture.Token)).Generation.Value.Should().Be(3);
        var unknown = () => service.ChangeLifecycleAsync(new(Guid.NewGuid()), new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await unknown.Should().ThrowAsync<InvalidDataException>();
    }

    internal static SessionWorkspaceService Service(InteractionStorageFixture fixture, Access access) =>
        new(fixture.Store, new(fixture.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);

    [WindowsFact]
    public async Task Maintained_task_query_is_bounded_paged_and_refuses_live_work_even_beyond_a_display_page()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var subject = fixture.Request.SessionId;
        for (var i = 0; i < 51; i++)
        {
            var request = InteractionStorageFixture.NewRequest(subject);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await fixture.Tasks.CommitAsync(intent, 0, fixture.Token);
            await fixture.Tasks.CommitAsync(intent.Next(HostTaskState.Succeeded), 1, fixture.Token);
        }
        var page = await fixture.Store.ReadTaskPageAsync(subject, null, 50, fixture.Token);
        page.Records.Should().HaveCount(50);
        page.Next.Should().NotBeNull();
        var tail = await fixture.Store.ReadTaskPageAsync(subject, page.Next, 50, fixture.Token);
        tail.Records.Should().HaveCount(2);
        tail.Next.Should().BeNull();
        page.Records.Concat(tail.Records).Select(record => record.Request.TaskId).Distinct().Should().HaveCount(52);
        var blocker = InteractionStorageFixture.NewRequest(subject);
        using (var root = HostActivity.BeginRoot(blocker, HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.Tasks.CommitAsync(new(blocker, new(1), HostTaskState.IntentRecorded), 0, fixture.Token);
        }
        var control = () => Service(fixture, new()).ChangeLifecycleAsync(subject, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await control.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
        (await fixture.Tasks.ReadTaskAsync(blocker.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [WindowsFact]
    public async Task Lifecycle_revision_race_and_task_admission_lease_have_one_serialized_truth()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishAsync(fixture, HostTaskState.Succeeded);
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.RunAsync(async () =>
            await fixture.Tasks.CommitAsync(new(fixture.Request, new(1), HostTaskState.IntentRecorded), 0, fixture.Token));
        async Task<bool> AttemptAsync()
        {
            try
            {
                await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token));
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        outcomes.Count(succeeded => succeeded).Should().Be(1);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(2);
        await FinishAsync(fixture, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            entered.Set();
            release.Wait(fixture.Token);
        };
        var service = Service(fixture, new());
        var resume = service.ChangeLifecycleAsync(fixture.Request.SessionId, new(2), true, RequestOrigin.LocalUi, fixture.Token);
        await Task.Run(() => entered.Wait(fixture.Token), fixture.Token);
        var concurrent = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        Task write;
        using (var root = HostActivity.BeginRoot(concurrent, HostActivityLayer.Application, HostOperation.Request))
        {
            write = fixture.Tasks.CommitAsync(new(concurrent, new(1), HostTaskState.IntentRecorded), 0, fixture.Token).AsTask();
            write.IsCompleted.Should().BeFalse("the lifecycle owns the task lease until authoritative COMMIT");
            release.Set();
            await write;
        }
        (await resume).Generation.Value.Should().Be(3);
        var blocked = () => service.ChangeLifecycleAsync(concurrent.SessionId, new(3), false, RequestOrigin.LocalUi, fixture.Token);
        await blocked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
    }

    internal static Task FinishAsync(InteractionStorageFixture fixture, HostTaskState state) =>
        fixture.RunAsync(async () =>
        {
            var prior = (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!;
            await fixture.Tasks.CommitAsync(prior.Next(state), prior.Revision.Value, fixture.Token);
        });

    internal sealed class Access : ISessionWorkspaceAccess, IEvidenceQueryAccess
    {
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
    }
}
