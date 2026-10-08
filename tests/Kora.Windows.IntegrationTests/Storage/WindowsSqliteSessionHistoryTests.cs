using System.Text;
using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionHistoryTests
{
    [WindowsFact]
    public async Task Ordered_final_answers_decisions_and_receipts_survive_restart_with_immutable_exact_citations()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await CreateQuestionAsync(fixture);
        var key = question.Question!.Key;
        var drafted = await fixture.RunAsync(() => fixture.Questions.DraftAsync(key, new([], "private draft"), RequestOrigin.LocalUi, fixture.Token));
        var before = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        before.Records.Should().NotContain(record => record.Answer == "private draft");
        await fixture.RunAsync(() => fixture.Questions.SubmitAsync(drafted.Question!.Key, new([], "Final answer"),
            RequestOrigin.LocalUi, fixture.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var page = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        page.Records.Select(row => row.Sequence).Should().Equal(Enumerable.Range(1, page.Records.Length).Select(value => (long)value));
        page.Records.Should().ContainSingle(record => record.Kind == SessionHistoryKind.Answer && record.Answer == "Final answer");
        page.Records.Should().Contain(record => record.TaskState == HostTaskState.Succeeded);
        page.Records.Should().Contain(record => record.Decision == HostInteractionOutcome.Answered);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var reopened = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", "") { History = reopened }))
            .Should().Be(Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", "") { History = page })));
        foreach (var row in page.Records)
        {
            var exact = await fixture.Store.ReadHistoryEventAsync(fixture.Request.SessionId, row.Id, fixture.Token);
            exact!.Id.Should().Be(row.Id);
            exact.ProvenanceDigest.Should().Be(row.ProvenanceDigest);
        }
    }

    [WindowsFact]
    public async Task Snapshot_pages_exclude_later_appends_and_cross_session_cursors_and_events_fail_closed()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await CreateQuestionAsync(fixture);
        var session = fixture.Request.SessionId;
        var first = await fixture.Store.ReadHistoryAsync(session, null, 1, fixture.Token);
        first.Next.Should().NotBeNull();
        await CreateQuestionAsync(fixture);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var rows = first.Records.ToList();
        var cursor = first.Next;
        while (cursor is not null)
        {
            var next = await fixture.Store.ReadHistoryAsync(session, cursor, 1, fixture.Token);
            next.Snapshot.Should().Be(first.Snapshot);
            rows.AddRange(next.Records);
            cursor = next.Next;
        }
        rows.Count.Should().Be((int)first.Snapshot);
        (await fixture.Store.ReadHistoryAsync(session, null, 50, fixture.Token)).Snapshot.Should().BeGreaterThan(first.Snapshot);
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        var wrongCursor = () => fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, first.Next, 1, fixture.Token).AsTask();
        await wrongCursor.Should().ThrowAsync<ArgumentException>();
        (await fixture.Store.ReadHistoryEventAsync(fixture.Request.SessionId, rows[0].Id, fixture.Token)).Should().BeNull();
        var unknown = () => fixture.Store.ReadHistoryAsync(new(Guid.NewGuid()), null, 1, fixture.Token).AsTask();
        await unknown.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Passive_native_and_current_name_typed_or_activated_history_never_append_or_resume_or_replay()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        await service.ChangeLifecycleAsync(fixture.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        var tasks = fixture.Count("host_task_events");
        var audits = fixture.Count("security_audit_events");
        var events = fixture.Count("session_history");
        foreach (var origin in new[] { RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice })
        {
            var command = SessionCommand.Parse($"Nova, session history {fixture.Request.SessionId.Value:D} limit 1", "Nova")!;
            var page = (await service.ExecuteCommandAsync(command, origin, () => true, fixture.Token)).History!;
            var exact = SessionCommand.Parse($"session get {fixture.Request.SessionId.Value:D} {page.Records[0].Id:D}", "Nova")!;
            (await service.ExecuteCommandAsync(exact, origin, () => true, fixture.Token)).HistoryEvent!.Id.Should().Be(page.Records[0].Id);
        }
        using var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        (await service.ReadHistoryAsync(fixture.Request.SessionId, null, 1, fixture.Token)).Generation.Value.Should().Be(2);
        fixture.Count("host_task_events").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Count("session_history").Should().Be(events);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.IsActive.Should().BeFalse();
        access.CanInspect = false;
        var denied = () => service.ReadHistoryAsync(fixture.Request.SessionId, null, 1, fixture.Token);
        await denied.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task Disposition_redacts_content_retains_citations_invalidates_snapshot_and_rejects_late_work()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await CreateQuestionAsync(fixture);
        await fixture.RunAsync(() => fixture.Questions.SubmitAsync(question.Question!.Key, new([], "Sensitive final answer"),
            RequestOrigin.LocalUi, fixture.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var page = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 1, fixture.Token);
        var all = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        using var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await service.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 0, fixture.Token);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var disposed = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        disposed.Disposed.Should().BeTrue();
        foreach (var row in all.Records)
        {
            var retained = disposed.Records.Single(record => record.Id == row.Id);
            retained.ProvenanceDigest.Should().Be(row.ProvenanceDigest);
            retained.Sequence.Should().Be(row.Sequence);
            retained.Availability.Should().Be(SessionHistoryAvailability.Redacted);
            retained.Question.Should().BeNull();
            retained.Answer.Should().BeNull();
        }
        var stalePage = () => fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, page.Next, 1, fixture.Token).AsTask();
        await stalePage.Should().ThrowAsync<InvalidOperationException>();
        var late = () => fixture.RunAsync(() => fixture.Questions.SubmitAsync(question.Question!.Key,
            new([], "late"), RequestOrigin.LocalUi, fixture.Token));
        await late.Should().ThrowAsync<InvalidDataException>();
        using var connection = fixture.OpenRaw();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT source || projection FROM session_history;";
        using var reader = read.ExecuteReader();
        while (reader.Read()) { reader.GetString(0).Should().NotContain("Sensitive final answer").And.NotContain("Host prompt"); }
    }

    [WindowsFact]
    public async Task V3_migration_has_explicit_baseline_gap_and_interruption_rolls_back_before_restart_retry()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await CreateQuestionAsync(fixture);
        fixture.Mutate("DROP TABLE session_history; DROP TABLE session_history_heads; PRAGMA user_version=3;");
        fixture.Reopen(new InteractionTransactionCheckpoint { Commit = (_, _) => throw new IOException("Injected migration interruption") });
        var interrupted = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await interrupted.Should().ThrowAsync<IOException>();
        fixture.Count("host_questions").Should().Be(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var page = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        page.Records[0].Kind.Should().Be(SessionHistoryKind.Gap);
        page.Records.Should().OnlyContain(row => row.Baseline);
        page.Records.Should().Contain(row => row.Question == "Host prompt");
    }

    [Theory]
    [InlineData("PRAGMA user_version=99;")]
    [InlineData("PRAGMA user_version=0;")]
    [InlineData("UPDATE session_history SET projection='{}';")]
    [InlineData("UPDATE session_history SET digest=printf('%064d',0);")]
    [InlineData("DELETE FROM session_history;")]
    [InlineData("DROP TABLE session_history;")]
    [InlineData("UPDATE session_history SET source=json_set(source,'$.LocalFilePreview','volatile file text');")]
    [InlineData("UPDATE session_history SET source=json_set(source,'$.SharedSkillSnapshot','volatile shared SKILL.md text');")]
    [InlineData("UPDATE session_history SET source=json_set(source,'$.SpeechCaption','retired caption text');")]
    public async Task Unknown_corrupt_and_volatile_surface_injection_never_becomes_history_or_model_authority(string mutation)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Mutate(mutation);
        var read = () => fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 1, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        fixture.Reopen();
        var reopen = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await reopen.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Cancellation_and_stale_concurrent_answers_leave_no_partial_or_duplicate_turns()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await CreateQuestionAsync(fixture);
        var count = fixture.Count("session_history");
        using var cancelled = new CancellationTokenSource();
        fixture.Reopen(new InteractionTransactionCheckpoint { Commit = (_, _) => cancelled.Cancel() });
        var cancel = () => fixture.RunAsync(() => fixture.Questions.SubmitAsync(question.Question!.Key,
            new([], "Cancelled answer"), RequestOrigin.LocalUi, cancelled.Token));
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        fixture.Count("session_history").Should().Be(count);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        await fixture.PublishAsync();
        Task<HostInteractionOutcome> Submit(string answer) => fixture.RunAsync(async () =>
            (await fixture.Questions.SubmitAsync(question.Question!.Key, new([], answer),
                RequestOrigin.LocalUi, fixture.Token)).Outcome);
        var results = await Task.WhenAll(Submit("A"), Submit("B"));
        results.Should().ContainSingle(outcome => outcome == HostInteractionOutcome.Answered);
        var history = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        history.Records.Should().ContainSingle(row => row.Kind == SessionHistoryKind.Answer);
        history.Records.Should().NotContain(row => row.Answer == "Cancelled answer");
    }

    private static Task<HostInteractionDecision> CreateQuestionAsync(InteractionStorageFixture fixture) =>
        fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Host prompt", QuestionKind.Text, [], maximumTextLength: 64),
            fixture.Time.Now.AddMinutes(10), fixture.Token));

    [WindowsFact]
    public async Task Over_budget_question_content_is_explicitly_unavailable_without_changing_citation_or_native_byte_bounds()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var options = Enumerable.Range(0, 32).Select(index => new QuestionOption(index.ToString(CultureInfo.InvariantCulture),
            new string('界', 512)));
        await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new(new string('界', 4096), QuestionKind.SingleChoice, options),
            fixture.Time.Now.AddMinutes(10), fixture.Token));
        var page = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        var question = page.Records.Single(row => row.Kind == SessionHistoryKind.Question);
        question.Availability.Should().Be(SessionHistoryAvailability.Unavailable);
        question.Question.Should().BeNull();
        question.Options.Should().BeEmpty();
        SessionCommandResult.Serialize(new("observed", SessionHistoryPage.Scope) { History = page })
            .Length.Should().BeLessThanOrEqualTo(SessionHistoryPage.MaximumBytes);
        var exact = await fixture.Store.ReadHistoryEventAsync(fixture.Request.SessionId, question.Id, fixture.Token);
        exact!.ProvenanceDigest.Should().Be(question.ProvenanceDigest);
        exact.Availability.Should().Be(SessionHistoryAvailability.Unavailable);
    }

    [WindowsFact]
    public async Task Concurrent_append_and_passive_read_share_one_committed_snapshot_not_uncommitted_history()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        await fixture.PublishAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            entered.SetResult();
            release.Wait(fixture.Token);
        };
        var append = CreateQuestionAsync(fixture);
        await entered.Task.WaitAsync(fixture.Token);
        var read = fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token).AsTask();
        var completedBeforeCommit = read.IsCompleted;
        release.Set();
        await append;
        var page = await read;
        completedBeforeCommit.Should().BeFalse();
        page.Records.Should().ContainSingle(row => row.Question == "Host prompt");
        page.Snapshot.Should().Be(page.Records[^1].Sequence);
    }
}
