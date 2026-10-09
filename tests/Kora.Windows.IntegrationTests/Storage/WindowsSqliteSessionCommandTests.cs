using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteSessionCommandTests
{
    [WindowsFact]
    public async Task Exact_commands_reopen_named_authority_isolate_identical_labels_and_preserve_grants_without_replay()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var scoped = await fixture.GrantAsync("session");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        Task<SessionCommandResult> Run(string command, RequestOrigin origin = RequestOrigin.LocalUi) =>
            service.ExecuteCommandAsync(SessionCommand.Parse(command, "Kora")!, origin, () => true, fixture.Token);
        var first = (await Run("session create \"Same\"")).Sessions.Single();
        var second = (await Run("session create \"Same\"", RequestOrigin.ActivatedVoice)).Sessions.Single();
        first.Id.Should().NotBe(second.Id);
        var renamed = (await Run($"session rename {first.Id:D} 1 1 \"Renamed\"")).Sessions.Single();
        renamed.Generation.Should().Be(1);
        renamed.MetadataRevision.Should().Be(2);
        (await Run($"session status {second.Id:D}")).Sessions.Single().Name.Should().Be("Same");
        var stale = () => Run($"session rename {first.Id:D} 1 1 \"Stale\"");
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var done = (await Run($"session done {first.Id:D} 1")).Sessions.Single();
        done.Active.Should().BeFalse();
        done.Generation.Should().Be(2);
        fixture.Reopen();
        service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        (await Run($"session status {first.Id:D}")).Sessions.Single().Name.Should().Be("Renamed");
        var resumed = (await Run($"session resume {first.Id:D} 2", RequestOrigin.ActivatedVoice)).Sessions.Single();
        resumed.Generation.Should().Be(3);
        (await fixture.Store.ReadQuestionsAsync(new(first.Id), fixture.Token)).Should().BeEmpty();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(scoped);
        var unknown = () => Run($"session status {Guid.NewGuid():D}");
        await unknown.Should().ThrowAsync<InvalidDataException>();
        var audits = fixture.Count("security_audit_events");
        (await Run($"session inspect {first.Id:D} tasks limit 1")).Tasks.Should().ContainSingle();
        (await Run($"session inspect {first.Id:D} questions")).Questions.Should().BeEmpty();
        fixture.Count("security_audit_events").Should().Be(audits, "read intents never change session authority");
        var list = await Run("session list limit 1");
        list.Sessions.Should().ContainSingle();
        list.Next.Should().NotBeNull();
        var tail = await Run($"session list after {list.Next:D} limit 50");
        tail.Sessions.Should().HaveCount(2).And.NotContain(row => row.Id == list.Sessions[0].Id);
    }

    [WindowsFact]
    public async Task Exact_lifecycle_commands_keep_nonterminal_and_Unknown_work_blockers()
    {
        foreach (var state in new[] { HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Unknown })
        {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        if (state != HostTaskState.IntentRecorded) { await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, state); }
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var act = () => service.ExecuteCommandAsync(
            SessionCommand.Parse($"session done {fixture.Request.SessionId.Value:D} 1", "Kora")!,
            RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(state);
        }
    }

    [WindowsFact]
    public async Task Question_blocker_and_origin_privacy_revision_race_remain_checked_by_actual_transaction()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var pending = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Pending", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var command = SessionCommand.Parse($"session done {fixture.Request.SessionId.Value:D} 1", "Kora")!;
        var blocked = () => service.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await blocked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unresolved questions*");
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token))
            .Single(question => question.Key.QuestionId == pending.Question!.Key.QuestionId).Status.Should().Be(QuestionStatus.Pending);
        var system = () => service.ExecuteCommandAsync(command, RequestOrigin.HostSystem, () => true, fixture.Token);
        await system.Should().ThrowAsync<InvalidOperationException>();
        var checkpoint = new InteractionTransactionCheckpoint();
        var allowed = true;
        fixture.Reopen(checkpoint);
        checkpoint.Commit = (_, _) => allowed = false;
        service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var rename = () => service.ExecuteCommandAsync(
            SessionCommand.Parse($"session rename {fixture.Request.SessionId.Value:D} 1 0 \"Denied\"", "Kora")!,
            RequestOrigin.ActivatedVoice, () => allowed, fixture.Token);
        await rename.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Metadata.Should().BeNull();
    }

    [WindowsFact]
    public async Task Cancelled_and_invalid_persisted_state_is_explicit_without_new_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var create = () => service.ExecuteCommandAsync(SessionCommand.Parse("session create \"Cancelled\"", "Kora")!,
            RequestOrigin.LocalUi, () => true, cancelled.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        fixture.Count("work_sessions").Should().Be(1);
        fixture.Mutate("UPDATE authority_head SET sequence=0;");
        fixture.Reopen();
        service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var read = () => service.ExecuteCommandAsync(new(SessionCommandOperation.Status, fixture.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
    }
}
