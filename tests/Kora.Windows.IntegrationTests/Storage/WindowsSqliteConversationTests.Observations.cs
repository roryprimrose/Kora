using System.Text;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData("audit")]
    [InlineData("source-commit")]
    [InlineData("cancel-commit")]
    public async Task ObservationAndAnswerRollbackTogetherWithoutLinkOrSuccessReceipt(string failure)
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
        var observation = StoredObservation(f);
        armed = true;
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance, new("answer", null))
        { Observations = new(1, [observation]) };
        await f.Store.Invoking(store => store.CompleteConversationExecutionAsync(request, result, () => live, cancellation.Token).AsTask())
            .Should().ThrowAsync<Exception>();
        armed = false;
        var message = await f.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
        message.Observations.Should().BeNull();
        message.AssistantResponse.Should().BeNull();
        message.Outcome.Should().BeNull();
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!.State.Should().Be(HostTaskState.DispatchRecorded);
        AuditText(f).Should().NotContain("private-observation-marker");
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("private-observation-marker");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionDeletionRemovesEmbeddedObservationBytesAndJournalWithoutDeletingIndependentAudit(bool due)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var perpetual = await f.GrantAsync("perpetual");
        await FinishInitial(f);
        var session = f.Request.SessionId;
        var request = Fresh(session);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "original", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            await f.Store.CompleteConversationExecutionAsync(request,
                new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance, new("answer", null))
                { Observations = new(1, [StoredObservation(f)]) }, static () => true, f.Token);
        }
        var audit = AuditText(f);
        if (due)
        {
            f.Time.Now = (await f.Store.ReadRetentionAsync(session, f.Token)).DeleteDue;
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Retention);
            (await f.Store.ApplyRetentionAsync(static () => true, static (_, _) => ValueTask.CompletedTask, f.Token)).Deleted.Should().Be(1);
        }
        else
        {
            var preview = await f.Store.PreviewDispositionAsync(session, new(1), 0, f.Token);
            var control = Fresh(session);
            using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        }
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("private-observation-marker");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        AuditText(f).Should().StartWith(audit).And.NotContain("private-observation-marker");
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Contain(perpetual);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await f.Store.Invoking(store => store.ReadConversationMessageAsync(session, request.RequestId, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LargeObservationCellsKeepUnchangedFullCellAndPageBudgetsWithoutDroppingAnyResult()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        for (var index = 0; index < 3; index++)
        {
            var request = Fresh(f.Request.SessionId);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "original", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            var observations = Enumerable.Range(0, 6).Select(_ => RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version,
                "{}", new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", new string('b', 3900))), f.Time.GetUtcNow()));
            var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance, new("answer", null))
            { Observations = new(1, [.. observations]) };
            var receipt = await f.Store.CompleteConversationExecutionAsync(request, result, static () => true, f.Token);
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(receipt.Message).Length.Should().BeLessThanOrEqualTo(32768);
            receipt.Message.Observations!.Items.Should().HaveCount(6);
        }
        var first = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 50, f.Token);
        first.Messages.Should().HaveCount(2);
        ConversationPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        var next = await f.Store.ReadConversationPageAsync(f.Request.SessionId, first.Next!, 50, f.Token);
        next.Messages.Should().ContainSingle();
        next.Next.Should().BeNull();
        first.Messages.Concat(next.Messages).Should().OnlyContain(message => message.Observations!.Items.Length == 6);
    }

    private static RetainedLocalObservation StoredObservation(InteractionStorageFixture f) =>
        RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version, "{}",
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "private-observation-marker")), f.Time.GetUtcNow());
}
