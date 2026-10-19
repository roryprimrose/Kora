using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task SessionDispositionRemovesCompleteAssistantPayloadAndJournalButPreservesUnrelatedOriginal()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        var request = Fresh(session);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "remove original marker", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            await f.Store.CompleteConversationExecutionAsync(request,
                new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new("remove assistant marker", null)),
                static () => true, f.Token);
        }
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await FinishInitial(f);
        var other = await Submit(f, f.Request.SessionId, "unrelated original retained");
        var preview = await f.Store.PreviewDispositionAsync(session, new(1), 0, f.Token);
        preview.ConversationMessages.Should().Be(1);
        var control = Fresh(session);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        }
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath))
            .Should().NotContain("remove original marker").And.NotContain("remove assistant marker");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        AuditText(f).Should().NotContain("remove assistant marker");
        (await f.Store.ReadConversationMessageAsync(other.Message.Request.SessionId, other.Message.Request.RequestId, f.Token))
            .Should().Be(other.Message);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await f.Store.Invoking(store => store.ReadConversationMessageAsync(session, request.RequestId, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PrivateAnswerTaskAndTypedAuditShareOneCompletionAndSurviveRestartWithoutChangingOriginalCountOrLegacyBytes()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var legacy = await Submit(f, f.Request.SessionId, "legacy original");
        var before = AuditText(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        ConversationSubmissionReceipt completed;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "genuine original USER", static () => true, f.Token);
            var dispatch = await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            dispatch.Task.State.Should().Be(HostTaskState.DispatchRecorded);
            var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome,
                provenance, new("private assistant unique marker e\u0301\r\n🙂", null));
            completed = await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
            completed.Task.State.Should().Be(HostTaskState.Succeeded);
            completed.Message.Revision.Should().Be(3);
            await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request, result,
                static () => true, f.Token).AsTask()).Should().ThrowAsync<Exception>("terminal completion is single-use");
        }
        AuditText(f).Should().StartWith(before).And.NotContain("private assistant unique marker").And.NotContain("genuine original USER");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token)).Should().BeEmpty();
        var page = await f.Store.ReadConversationPageAsync(request.SessionId, null, 25, f.Token);
        page.Messages.Should().HaveCount(2, "answers do not consume original USER slots");
        page.Messages[0].Should().Be(legacy.Message);
        page.Messages[1].Should().Be(completed.Message);
        ConversationPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        (await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token)).Should().Be(completed.Message);
        var work = await f.Store.ReadWorkAsync(request.SessionId, 0, new(), f.Token);
        System.Text.Json.JsonSerializer.Serialize(work).Should().NotContain("private assistant unique marker");
    }

    [Fact]
    public async Task RestartAfterDispatchRecoversUnknownWithoutAnAnswerAndNeverReplaysProviderWork()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "interrupted model original", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token)).Single().State.Should().Be(HostTaskState.Unknown);
        var message = await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
        message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        message.Reason.Should().Be(ModelTurnReason.ProviderOutcome);
        message.AssistantResponse.Should().BeNull();
        message.Execution.Should().Be(provenance);
        message.Revision.Should().Be(3);
        f.Count("conversation_messages").Should().Be(1);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("source-commit")]
    [InlineData("cancel-commit")]
    public async Task FailedTerminalAuditSourceOrCancellationRollsBackBothAnswerAndTask(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var armed = false;
        var live = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        f.Reopen(new AnswerCheckpoint(
            () => { if (armed && failure is "audit") { throw new IOException("required audit failure"); } },
            () =>
            {
                if (!armed) { return; }
                if (failure is "source-commit") { live = false; }
                if (failure is "cancel-commit") { cancellation.Cancel(); }
            }));
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, "original", () => live, f.Token);
        await f.Store.BeginConversationExecutionAsync(request, provenance, 0, () => live, f.Token);
        armed = true;
        await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, provenance, new("must not commit private answer", null)),
            () => live, cancellation.Token).AsTask()).Should().ThrowAsync<Exception>();
        armed = false;
        var message = await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
        message.AssistantResponse.Should().BeNull();
        message.Outcome.Should().BeNull();
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!.State.Should().Be(HostTaskState.DispatchRecorded);
        AuditText(f).Should().NotContain("must not commit private answer");
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("must not commit private answer");
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Denied)]
    public async Task NonSuccessCannotRetainAnswerAndDispatchCancellationKeepsUnknownCertainty(ModelTurnOutcome outcome)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, "original", static () => true, f.Token);
        await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
        await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request,
            new(outcome, ModelTurnReason.ProviderOutcome, provenance, new("forbidden success-shaped answer", null)),
            static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
        var receipt = await f.Store.CompleteConversationExecutionAsync(request,
            new(outcome, ModelTurnReason.ProviderOutcome, provenance), static () => true, f.Token);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(outcome == ModelTurnOutcome.Denied ? HostTaskState.Denied : HostTaskState.Unknown);
    }

    private sealed class AnswerCheckpoint(Action audit, Action commit) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) => audit();
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => commit();
    }
}
