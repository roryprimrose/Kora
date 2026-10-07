using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteTaskControlTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Exact_current_wait_cancellation_is_atomic_terminal_reopens_without_replay_and_preserves_other_authority(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var original = f.Request;
        var question = await WaitAsync(f);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var inspected = await service.ExecuteCommandAsync(new(SessionCommandOperation.TaskInspect, original.SessionId.Value)
            { TaskId = original.TaskId.Value }, origin, () => true, f.Token);
        var observed = inspected.TaskDetails.Single();
        observed.CurrentSource.Should().BeTrue();
        observed.Source.Should().Be(LocalVersionWait.Source);
        observed.Task.State.Should().Be(HostTaskState.IntentRecorded);
        observed.Question!.Key.Should().Be(question.Key);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var independent = await f.GrantAsync("perpetual");
        var cancelled = await service.CancelTaskAsync(Target(observed), origin, () => true, f.Token);
        cancelled.Task.State.Should().Be(HostTaskState.Cancelled);
        cancelled.Task.Revision.Value.Should().Be(2);
        cancelled.Question!.Status.Should().Be(QuestionStatus.Cancelled);
        cancelled.Question.Key.Revision.Value.Should().Be(2);
        (await f.Tasks.ReadTaskAsync(original.TaskId, f.Token)).Should().Be(cancelled.Task);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(independent);
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
        f.Reopen();
        var reopened = await f.Store.ReadTaskAsync(original.SessionId, original.TaskId, f.Token);
        reopened!.Task.Should().Be(cancelled.Task);
        reopened.CurrentSource.Should().BeFalse();
        (await new HostTaskCoordinator(f.Tasks).RecoverAsync(10, f.Token)).Should().NotContain(row => row.Request == original);
        var repeat = () => WindowsSqliteSessionWorkspaceTests.Service(f, new()).CancelTaskAsync(Target(cancelled), origin, () => true, f.Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(independent);
        using var host = HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Request);
        var lateAnswer = () => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token).AsTask();
        await lateAnswer.Should().ThrowAsync<InvalidDataException>();
        var lateDispatch = () => new HostTaskCoordinator(f.Tasks).RecordDispatchAsync(new(original, new(1), HostTaskState.IntentRecorded), f.Token).AsTask();
        await lateDispatch.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("task")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("question")]
    [InlineData("question-revision")]
    [InlineData("session")]
    [InlineData("answered")]
    [InlineData("dispatched")]
    [InlineData("unknown")]
    [InlineData("reopened")]
    [InlineData("expired")]
    [InlineData("privacy")]
    [InlineData("additional-question")]
    public async Task Stale_foreign_closed_or_lost_authority_never_becomes_cancellation(string scenario)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WaitAsync(f);
        var observed = (await f.Store.ReadTaskAsync(f.Request.SessionId, f.Request.TaskId, f.Token))!;
        var target = Target(observed);
        target = scenario switch
        {
            "task" => target with { TaskId = new(Guid.NewGuid()) },
            "revision" => target with { TaskRevision = new(2) },
            "generation" => target with { Generation = new(2) },
            "question" => target with { QuestionId = new(Guid.NewGuid()) },
            "question-revision" => target with { QuestionRevision = new(2) },
            "session" => target with { SessionId = new(Guid.NewGuid()) },
            _ => target,
        };
        if (scenario is "answered" or "dispatched" or "unknown")
        {
            var answer = await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
            if (scenario is "dispatched" or "unknown")
            {
                var dispatch = await f.RunAsync(() => f.Store.AdmitVersionDispatchAsync(answer.Question!.Key, () => true, f.Token));
                if (scenario is "unknown") { await f.RunAsync(() => new HostTaskCoordinator(f.Tasks).RecordOutcomeAsync(dispatch, HostTaskState.Unknown, f.Token)); }
            }
        }
        if (scenario is "reopened") { f.Reopen(); }
        if (scenario is "expired") { f.Time.Now = question.ExpiresAt; }
        if (scenario is "additional-question")
        {
            await f.RunAsync(() => f.Questions.CreateAsync(f.Request, LocalVersionWait.CreateSpec(),
                f.Time.Now.AddMinutes(5), f.Token));
        }
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var before = await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token);
        var cancel = () => service.CancelTaskAsync(target, RequestOrigin.LocalUi,
            () => !string.Equals(scenario, "privacy", StringComparison.Ordinal), f.Token);
        await cancel.Should().ThrowAsync<Exception>();
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(before);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("storage")]
    [InlineData("gate")]
    [InlineData("cancel-token")]
    public async Task Required_audit_storage_and_commit_boundary_failures_roll_back_task_and_question_together(string scenario)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var question = await WaitAsync(f);
        var observed = (await f.Store.ReadTaskAsync(f.Request.SessionId, f.Request.TaskId, f.Token))!;
        var audits = f.Count("security_audit_events");
        var allowed = true;
        using var token = new CancellationTokenSource();
        checkpoint.Audit = (_, _) => { if (scenario is "audit") { throw new IOException("Required audit unavailable."); } };
        checkpoint.Commit = (_, _) =>
        {
            if (scenario is "storage") { throw new IOException("Before COMMIT."); }
            if (scenario is "gate") { allowed = false; }
            if (scenario is "cancel-token") { token.Cancel(); }
        };
        var cancel = () => WindowsSqliteSessionWorkspaceTests.Service(f, new()).CancelTaskAsync(Target(observed),
            RequestOrigin.ActivatedVoice, () => allowed, token.Token);
        await cancel.Should().ThrowAsync<Exception>();
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(observed.Task);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Key.Should().Be(question.Key);
        f.Count("security_audit_events").Should().Be(audits);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("stopped")]
    [InlineData("system")]
    [InlineData("original-request")]
    [InlineData("null-gate")]
    public async Task Raw_cancellation_rejects_missing_expired_foreign_or_non_original_control_authority(string scenario)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WaitAsync(f);
        var observed = (await f.Store.ReadTaskAsync(f.Request.SessionId, f.Request.TaskId, f.Token))!;
        var control = scenario is "original-request" ? f.Request : new HostRequest(new(Guid.NewGuid()),
            f.Request.SessionId, new(Guid.NewGuid()), scenario is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (scenario is not "original-request")
        {
            using var admitted = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(control, f.Token);
        }
        var audits = f.Count("security_audit_events");
        using var host = scenario is "missing" ? null
            : HostActivity.BeginRoot(scenario is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi) : control,
                HostActivityLayer.Application, HostOperation.Request);
        if (scenario is "stopped") { host!.Activity!.Stop(); }
        var cancel = () => f.Store.CancelWaitingTaskAsync(control, Target(observed),
            scenario is "null-gate" ? null! : () => true, f.Token).AsTask();
        if (scenario is "null-gate") { await cancel.Should().ThrowAsync<ArgumentNullException>(); }
        else { await cancel.Should().ThrowAsync<InvalidOperationException>(); }
        (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token)).Should().Be(observed.Task);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        f.Count("security_audit_events").Should().Be(audits);
    }

    [WindowsFact]
    public async Task Competing_answer_cancel_and_admission_have_one_truthful_winner()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WaitAsync(f);
        var observed = (await f.Store.ReadTaskAsync(f.Request.SessionId, f.Request.TaskId, f.Token))!;
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        var cancel = service.CancelTaskAsync(Target(observed), RequestOrigin.LocalUi, () => true, f.Token);
        var answer = f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
        try { await cancel; } catch (InvalidOperationException) { }
        try { await answer; } catch (InvalidDataException) { }
        var final = (await f.Store.ReadTaskAsync(f.Request.SessionId, f.Request.TaskId, f.Token))!;
        if (final.Task.State == HostTaskState.Cancelled)
        {
            final.Question!.Status.Should().Be(QuestionStatus.Cancelled);
        }
        else
        {
            final.Question!.Status.Should().Be(QuestionStatus.Answered);
            var dispatched = await f.RunAsync(() => f.Store.AdmitVersionDispatchAsync(final.Question.Key, () => true, f.Token));
            dispatched.State.Should().Be(HostTaskState.DispatchRecorded);
        }
    }

    internal static async Task<HostQuestionRecord> WaitAsync(InteractionStorageFixture f)
    {
        f.ObservationRevision = (await f.RunAsync(() => f.Store.PublishTrustedSnapshotAsync(f.Request,
            new(true, false, false, true), null, f.ObservationRevision, f.Token))).Value;
        var question = (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, LocalVersionWait.CreateSpec(),
            f.Time.Now.AddMinutes(5), f.Token))).Question!;
        await f.RunAsync(async () => await f.Store.AdmitVersionWaitAsync(question.Key, f.Token));
        return question;
    }

    internal static HostTaskCancellationTarget Target(HostTaskObservation observation) =>
        new(observation.Task.Request.SessionId, observation.Task.Request.TaskId, observation.Task.Revision,
            observation.Generation, observation.Question!.Key.QuestionId, observation.Question.Key.Revision);
}
