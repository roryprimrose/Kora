using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteHistorySearchTests
{
    [Theory]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.AnswerChannel',999);")]
    [InlineData("UPDATE session_history SET digest=printf('%064d',0);")]
    [InlineData("DELETE FROM session_history;")]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.SourceRevision',-1);")]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.AuditSequence',0);")]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.RequestId','00000000-0000-0000-0000-000000000000');")]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.TaskId','00000000-0000-0000-0000-000000000000');")]
    [InlineData("UPDATE session_history SET projection=json_set(projection,'$.SourceId','00000000-0000-0000-0000-000000000000');")]
    public async Task Corrupt_or_unavailable_nonmatching_receipts_fail_before_metadata_exclusion(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteTaskControlTests.WaitAsync(f);
        f.Mutate(mutation);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var act = () => service.SearchHistoryAsync(f.Request.SessionId, "nonmatching", null, 1, f.Token,
            new(Kind: SessionHistoryKind.Answer, Decision: HostInteractionOutcome.Approved), new(MaximumSequence: 1),
            new(SourceId: Guid.NewGuid()));
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Lexical_search_reads_real_committed_receipts_not_drafts_and_preserves_schema_activity_and_authority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await f.RunAsync(() => f.Questions.CreateAsync(f.Request,
            new("Café host prompt", QuestionKind.Text, [], maximumTextLength: 64), f.Time.Now.AddMinutes(10), f.Token));
        var draft = await f.RunAsync(() => f.Questions.DraftAsync(question.Question!.Key, new([], "private draftword"),
            RequestOrigin.LocalUi, f.Token));
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        (await service.SearchHistoryAsync(f.Request.SessionId, "draftword", null, 50, f.Token)).Records.Should().BeEmpty();
        await f.RunAsync(() => f.Questions.SubmitAsync(draft.Question!.Key, new([], "finalword"),
            RequestOrigin.LocalUi, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        var first = await service.SearchHistoryAsync(f.Request.SessionId, "cafe\u0301", null, 1, f.Token);
        first.Records.Should().ContainSingle().Which.Question.Should().Be("Café host prompt");
        first.Next.Should().NotBeNull();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        var next = await service.SearchHistoryAsync(f.Request.SessionId, "CAFÉ", first.Next, 50, f.Token);
        next.Snapshot.Should().Be(first.Snapshot);
        next.Records.Should().OnlyContain(row => row.Sequence > first.Records[0].Sequence);
        var answer = (await service.SearchHistoryAsync(f.Request.SessionId, "finalword", null, 50, f.Token)).Records.Single();
        answer.Should().Be(history.Records.Single(row => row.Kind == SessionHistoryKind.Answer));
        answer.RequestId.Should().NotBeNull();
        answer.TaskId.Should().NotBeNull();
        answer.SourceId.Should().NotBeNull();
        var correlation = new SessionHistoryCorrelationFilter(answer.RequestId, answer.TaskId, answer.SourceId);
        for (var mask = 0; mask < 8; mask++)
        {
            var filter = new SessionHistoryCorrelationFilter((mask & 1) != 0 ? answer.RequestId : null,
                (mask & 2) != 0 ? answer.TaskId : null, (mask & 4) != 0 ? answer.SourceId : null);
            (await service.SearchHistoryAsync(f.Request.SessionId, "absent finalword", null, 50, f.Token,
                new(Kind: SessionHistoryKind.Answer), new(answer.Sequence, answer.Sequence), filter))
                .Records.Should().ContainSingle().Which.Should().Be(answer);
        }
        foreach (var filter in new[] { correlation with { RequestId = Guid.NewGuid() },
            correlation with { TaskId = Guid.NewGuid() }, correlation with { SourceId = Guid.NewGuid() } })
        {
            (await service.SearchHistoryAsync(f.Request.SessionId, "finalword", null, 50, f.Token, correlation: filter))
                .Records.Should().BeEmpty();
        }
        foreach (var missing in history.Records)
        {
            foreach (var filter in new[] {
                missing.RequestId is null ? new SessionHistoryCorrelationFilter(answer.RequestId) : default,
                missing.TaskId is null ? new SessionHistoryCorrelationFilter(TaskId: answer.TaskId) : default,
                missing.SourceId is null ? new SessionHistoryCorrelationFilter(SourceId: answer.SourceId) : default }.Where(x => x != default))
            {
                (await service.SearchHistoryAsync(f.Request.SessionId, missing.Kind.ToString(), null, 50, f.Token,
                    range: new(missing.Sequence, missing.Sequence), correlation: filter)).Records.Should().BeEmpty();
            }
        }
        foreach (var missingAudit in history.Records.Where(row => row.AuditSequence is null))
        {
            var exact = new SessionHistoryRangeFilter(missingAudit.Sequence, missingAudit.Sequence, MinimumAuditSequence: 1);
            (await service.SearchHistoryAsync(f.Request.SessionId, missingAudit.Kind.ToString(), null, 50, f.Token,
                range: exact)).Records.Should().BeEmpty();
        }
        var range = new SessionHistoryRangeFilter(answer.Sequence, answer.Sequence,
            answer.Generation.Value, answer.Generation.Value, answer.SourceRevision, answer.SourceRevision,
            answer.AuditSequence, answer.AuditSequence);
        (await service.SearchHistoryAsync(f.Request.SessionId, "absent finalword", null, 50, f.Token,
            new(Kind: SessionHistoryKind.Answer), range)).Records.Should().ContainSingle().Which.Should().Be(answer);
        (await service.SearchHistoryAsync(f.Request.SessionId, "finalword", null, 50, f.Token,
            range: range with { MinimumSequence = answer.Sequence + 1, MaximumSequence = null })).Records.Should().BeEmpty();
        var metadata = new SessionHistoryMetadataFilter(Kind: SessionHistoryKind.Answer,
            QuestionStatus: QuestionStatus.Answered, AnswerChannel: RequestOrigin.LocalUi);
        (await service.SearchHistoryAsync(f.Request.SessionId, "absent finalword", null, 50, f.Token, metadata))
            .Records.Should().ContainSingle().Which.Should().Be(answer);
        (await service.SearchHistoryAsync(f.Request.SessionId, "finalword", null, 50, f.Token,
            metadata with { AnswerChannel = RequestOrigin.ActivatedVoice })).Records.Should().BeEmpty();
        (await service.SearchHistoryAsync(f.Request.SessionId, "Succeeded", null, 50, f.Token,
            new(Kind: SessionHistoryKind.Task, TaskState: HostTaskState.Succeeded)))
            .Records.Should().ContainSingle();
        (await service.SearchHistoryAsync(f.Request.SessionId, "Answered", null, 50, f.Token,
            new(Kind: SessionHistoryKind.Decision, Decision: HostInteractionOutcome.Answered)))
            .Records.Should().ContainSingle();
        var resolved = await service.ReadHistoryDetailAsync(f.Request.SessionId, answer.Id, f.Token);
        resolved.Reference.ItemId.Value.Should().Be(answer.Id);
        await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_task_events");
        var receipts = f.Count("session_history");
        foreach (var origin in new[] { RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice })
        {
            var command = SessionCommand.Parse($"Nova, session search {f.Request.SessionId.Value:D} limit 50 \"finalword\"", "Nova")!;
            (await service.ExecuteCommandAsync(command with { HistoryCorrelation = correlation }, origin, () => true, f.Token)).HistorySearch!.Records
                .Should().ContainSingle();
        }
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_task_events").Should().Be(tasks);
        f.Count("session_history").Should().Be(receipts);
        var stale = () => service.SearchHistoryAsync(f.Request.SessionId, "café", first.Next, 50, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        access.CanInspect = false;
        var denied = () => service.SearchHistoryAsync(f.Request.SessionId, "finalword", null, 50, f.Token);
        await denied.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task Source_disposition_and_foreign_ids_never_publish_old_content_or_select_authority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, new());
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var correlation = new SessionHistoryCorrelationFilter(f.Request.RequestId.Value, f.Request.TaskId.Value);
        var before = await service.SearchHistoryAsync(f.Request.SessionId, "question task", null, 1, f.Token, correlation: correlation);
        var preview = await service.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
        var after = await service.SearchHistoryAsync(f.Request.SessionId, "question task", null, 50, f.Token, correlation: correlation);
        after.Disposed.Should().BeTrue();
        after.Records.Should().BeEmpty();
        after.Gaps.Should().Be(after.Scanned);
        var filtered = await service.SearchHistoryAsync(f.Request.SessionId, "question task", null, 50, f.Token,
            new(Kind: SessionHistoryKind.Answer, QuestionStatus: QuestionStatus.Answered, AnswerChannel: RequestOrigin.LocalUi));
        filtered.Records.Should().BeEmpty();
        filtered.Gaps.Should().Be(after.Gaps);
        filtered.Scanned.Should().Be(after.Scanned);
        var stale = () => service.SearchHistoryAsync(f.Request.SessionId, "question task", before.Next, 50, f.Token, correlation: correlation);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var foreign = () => service.SearchHistoryAsync(new(Guid.NewGuid()), "question task", before.Next, 50, f.Token, correlation: correlation);
        await foreign.Should().ThrowAsync<ArgumentException>();
        var unknown = () => service.SearchHistoryAsync(new(Guid.NewGuid()), "question task", null, 50, f.Token);
        await unknown.Should().ThrowAsync<InvalidDataException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => service.SearchHistoryAsync(f.Request.SessionId, "question", null, 50, cancelled.Token, correlation: correlation);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }
}
