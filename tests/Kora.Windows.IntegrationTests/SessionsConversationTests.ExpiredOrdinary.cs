using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeExpiredOrdinaryReviewDeclineAndFreshConfirmationRetainSeparateGenuineAnswerWithoutChangingOriginal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            f.Time.UseFrozenTimers = true;
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var source = await ExpireNativeOrdinaryAsync(f, composed, adapter, target);
            var oldTask = await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token);
            var snapshot = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            await composed.Conversation.ResumePendingConversationAsync(target, snapshot.Revision, () => true, f.Token);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            model.CaptureFreshOrdinaryInput = () => () => true;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single(item => item.Request == source.Request);
                model.ComposerBody = "unrelated unsent input";
                var messages = f.Count("conversation_messages");
                var audits = f.Count("security_audit_events");
                Click("ReviewExpiredOrdinary");
                await window.ConversationOperation;
                var text = window.FindControl<NamedTextBlock>("ExpiredOrdinaryReview")!.Text;
                text.Should().Contain(source.Body).And.Contain("before dispatch").And.Contain("NEW initiator: LocalUi")
                    .And.Contain("unknown (not inferred)").And.Contain("CURRENT provider");
                f.Count("conversation_messages").Should().Be(messages);
                f.Count("security_audit_events").Should().Be(audits);
                adapter.Calls.Should().Be(1);
                Click("CancelExpiredOrdinary");
                model.CanConfirmExpiredOrdinary.Should().BeFalse();
                f.Count("conversation_messages").Should().Be(messages);
                Click("ReviewExpiredOrdinary");
                await window.ConversationOperation;
                Click("ConfirmExpiredOrdinary");
                var operation = window.ConversationOperation;
                var fresh = await adapter.NextAsync();
                fresh.Body.Should().Be(source.Body);
                fresh.Context.GetProperty("Tools").GetArrayLength().Should().Be(0);
                fresh.Context.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                fresh.Provenance.Request.RequestId.Should().NotBe(source.Request.RequestId);
                fresh.Provenance.Request.TaskId.Should().NotBe(source.Request.TaskId);
                fresh.Provenance.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                fresh.Release();
                await operation;
                var actual = await f.Store.ReadConversationMessageAsync(target.Authority.SessionId,
                    fresh.Provenance.Request.RequestId, f.Token);
                actual.AssistantResponse!.Body.Should().Be("answer: " + source.Body);
                actual.Sequence.Should().Be(source.Sequence + 1);
                actual.OriginalTextSource.Should().Be(source.Request);
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, source.Request.RequestId, f.Token)).Should().Be(source);
                (await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token)).Should().Be(oldTask);
                model.ComposerBody.Should().Be("unrelated unsent input");
                Click("ConfirmExpiredOrdinary");
                await window.ConversationOperation;
                adapter.Calls.Should().Be(2);
                f.Count("conversation_messages").Should().Be(messages + 1);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(source.Body).And.NotContain("answer: ");
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); }

            void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("call")]
    [InlineData("capture")]
    [InlineData("owner")]
    [InlineData("same-byte-buffer")]
    [InlineData("selection")]
    [InlineData("cancel-new-review")]
    [InlineData("old-click-new-review")]
    public async Task NativeFreshExpiredReviewRejectsInputAndSourceRacesWithoutNewRequestOrProvider(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            f.Time.UseFrozenTimers = true;
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var source = await ExpireNativeOrdinaryAsync(f, composed, adapter, target);
            var snapshot = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            await composed.Conversation.ResumePendingConversationAsync(target, snapshot.Revision, () => true, f.Token);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            var inputEpoch = 0;
            model.CaptureFreshOrdinaryInput = () =>
            {
                var epoch = inputEpoch;
                return () => inputEpoch == epoch;
            };
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single(item => item.Request == source.Request);
                model.ComposerBody = "independent unsent";
                await model.ReviewExpiredOrdinaryAsync();
                var oldClick = model.CaptureExpiredOrdinaryDecision(confirm: true);
                if (fault is "input" or "call" or "capture" or "owner") { inputEpoch++; }
                if (fault is "same-byte-buffer") { model.ComposerBody = model.ComposerBody; }
                if (fault is "selection") { model.SelectedAnswerRecord = null; }
                if (fault is "cancel-new-review" or "old-click-new-review")
                {
                    model.CancelExpiredOrdinary();
                    await model.ReviewExpiredOrdinaryAsync();
                    if (fault is "cancel-new-review") { await model.CaptureExpiredOrdinaryDecision(confirm: false)(); }
                }
                await oldClick();
                f.Count("conversation_messages").Should().Be(2);
                adapter.Calls.Should().Be(1);
                model.ComposerBody.Should().Be("independent unsent");
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, source.Request.RequestId, f.Token)).Should().Be(source);
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); }
        });
    }

    private static async Task<ConversationMessage> ExpireNativeOrdinaryAsync(InteractionStorageFixture f,
        Composition composed, PendingTurnAdapter adapter, SessionWorkspaceEntry target)
    {
        var first = await composed.Conversation.SubmitQueuedAsync(target, "earlier active", () => true, f.Token);
        var active = await adapter.NextAsync();
        var pending = await composed.Conversation.SubmitQueuedAsync(target, "  exact expired e\u0301\r\nUSER body  ",
            () => true, f.Token, localAnswer: true);
        pending.Position.Should().Be(1);
        f.Time.Now = f.Time.Now.AddSeconds(15);
        active.Release();
        await first.Completion;
        var result = (await pending.Completion).RequireReceipt();
        result.Message.OrdinaryText.Should().BeTrue();
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
        result.Message.Execution.Should().BeNull();
        result.Task.State.Should().Be(HostTaskState.Denied);
        adapter.Calls.Should().Be(1);
        return result.Message;
    }
}
