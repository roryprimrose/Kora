using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteHistorySearchTests
{
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
            (await service.ExecuteCommandAsync(command, origin, () => true, f.Token)).HistorySearch!.Records
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
        var before = await service.SearchHistoryAsync(f.Request.SessionId, "question task", null, 1, f.Token);
        var preview = await service.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
        var after = await service.SearchHistoryAsync(f.Request.SessionId, "question task", null, 50, f.Token);
        after.Disposed.Should().BeTrue();
        after.Records.Should().BeEmpty();
        after.Gaps.Should().Be(after.Scanned);
        var stale = () => service.SearchHistoryAsync(f.Request.SessionId, "question task", before.Next, 50, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var foreign = () => service.SearchHistoryAsync(new(Guid.NewGuid()), "question task", before.Next, 50, f.Token);
        await foreign.Should().ThrowAsync<ArgumentException>();
        var unknown = () => service.SearchHistoryAsync(new(Guid.NewGuid()), "question task", null, 50, f.Token);
        await unknown.Should().ThrowAsync<InvalidDataException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => service.SearchHistoryAsync(f.Request.SessionId, "question", null, 50, cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }
}
