using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePendingOnlyClearReviewsCompleteSetThenRetainsCancelledOriginalsWithoutStoppingEitherSession(bool manualHold)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var other = f.Request.SessionId;
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var window = new SessionsWindow(composed.Model);
            window.Show();
            try
            {
                var model = composed.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                var target = model.SelectedSessionRecord!;
                var otherTarget = model.Sessions.Single(item => item.Authority.SessionId == other);
                var active = await composed.Conversation.SubmitQueuedAsync(target, "running original", () => true, f.Token);
                await adapter.NextAsync();
                var b = await composed.Conversation.SubmitQueuedAsync(target, "private pending B", () => true, f.Token);
                var c = await composed.Conversation.SubmitQueuedAsync(target, "private pending C", () => true, f.Token);
                var otherActive = await composed.Conversation.SubmitQueuedAsync(otherTarget, "other original", () => true, f.Token);
                (await otherActive.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Unavailable);
                var otherPending = await composed.Conversation.SubmitQueuedAsync(otherTarget, "other pending", () => true, f.Token);
                foreach (var refused in new[]
                {
                    (Request: active.Receipt.Message.Request, Expected: target),
                    (Request: b.Receipt.Message.Request, Expected: target with
                    { Metadata = new SessionMetadata(original, new(99), new("not the reviewed metadata")) }),
                })
                {
                    using var root = HostActivity.BeginRoot(refused.Request, HostActivityLayer.Desktop, HostOperation.Request);
                    await f.Store.Invoking(store => store.CancelPendingConversationSubmissionAsync(refused.Request,
                        refused.Expected, () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
                }
                (await f.Store.ReadTaskAsync(original, b.Receipt.Message.Request.TaskId, f.Token))!.Task.State
                    .Should().Be(HostTaskState.IntentRecorded);
                (await f.Store.ReadTaskAsync(original, active.Receipt.Message.Request.TaskId, f.Token))!.Task.State
                    .Should().Be(HostTaskState.DispatchRecorded);
                var retentionBefore = await f.Store.ReadRetentionAsync(original, f.Token);
                var runningBefore = await composed.Conversation.ReadRunningConversationAsync(target, () => true, f.Token);
                var panel = window.GetLogicalDescendants().OfType<PendingConversationPanel>().Single();
                await model.RefreshPendingConversationAsync();
                if (manualHold)
                {
                    panel.FindControl<Button>("HoldDispatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await panel.Operation;
                    model.CanReleaseConversationDispatch.Should().BeTrue();
                }
                var auditBefore = WindowsSqliteConversationTests.AuditText(f);
                var resources = composed.Audit.Events.Count(item => item.ActionId is "runtime.resource.acquire");
                panel.FindControl<Button>("ReviewPendingClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.CanConfirmPendingClear.Should().BeTrue();
                model.PendingClearText.Should().Contain(b.Receipt.Message.Request.RequestId.Value.ToString("D"))
                    .And.Contain(c.Receipt.Message.Request.RequestId.Value.ToString("D"))
                    .And.NotContain("private pending B").And.NotContain("other pending");
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditBefore);
                panel.FindControl<Button>("DiscardPendingClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanConfirmPendingClear.Should().BeFalse();
                b.Completion.IsCompleted.Should().BeFalse();
                (await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token)).Paused.Should().BeFalse();
                panel.FindControl<Button>("ReviewPendingClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                using var observation = HostActivity.BeginRoot(active.Receipt.Message.Request,
                    HostActivityLayer.Application, HostOperation.Request);
                var tasksBeforeObservation = f.Count("host_task_events");
                var messagesBeforeObservation = f.Count("conversation_messages");
                var remainingBefore = await composed.Conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
                remainingBefore.Ordinary.Turns.Where(turn => turn.Phase == ConversationPendingPhase.Pending)
                    .Select(turn => turn.Request).Should().Equal(b.Receipt.Message.Request, c.Receipt.Message.Request);
                remainingBefore.Format().Should().Contain("Submitted FIFO position 1").And.Contain("Submitted FIFO position 2")
                    .And.NotContain("private pending");
                composed.Conversation.IsRemainingWorkCurrent(remainingBefore, () => true).Should().BeTrue();
                model.CanConfirmPendingClear.Should().BeTrue("passive remaining-work inspection must not consume the clear review");
                f.Count("host_task_events").Should().Be(tasksBeforeObservation);
                f.Count("conversation_messages").Should().Be(messagesBeforeObservation);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditBefore);
                panel.FindControl<Button>("ConfirmPendingClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.PendingClearText.Should().Contain("reviewed-pending-cancelled");
                model.CanConfirmPendingClear.Should().BeFalse();
                composed.Conversation.IsRemainingWorkCurrent(remainingBefore, () => true).Should().BeFalse();
                await composed.Conversation.Invoking(service => service.ValidateRemainingWorkAsync(remainingBefore, () => true, f.Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
                var auditAfterClear = WindowsSqliteConversationTests.AuditText(f);
                var tasksAfterClear = f.Count("host_task_events");
                var remainingAfter = await composed.Conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
                remainingAfter.Ordinary.Turns.Should().NotContain(turn => turn.Phase == ConversationPendingPhase.Pending);
                remainingAfter.Ordinary.Running!.Target.Request.Should().Be(active.Receipt.Message.Request);
                remainingAfter.Ordinary.Paused.Should().BeTrue();
                remainingAfter.Ordinary.ManualHoldReason.Should()
                    .Be(manualHold ? ConversationDispatchHoldReason.OriginalUser : ConversationDispatchHoldReason.None);
                remainingAfter.Questions.Should().Equal(remainingBefore.Questions);
                remainingAfter.Format().Should().Contain("Pending ordinary FIFO entries: 0")
                    .And.Contain("Current ordinary work: running").And.NotContain("Submitted FIFO position");
                f.Count("host_task_events").Should().Be(tasksAfterClear);
                f.Count("conversation_messages").Should().Be(messagesBeforeObservation);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditAfterClear);
                await model.RefreshPendingConversationAsync();
                var cleared = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                cleared.ManualHoldReason.Should().Be(manualHold ? ConversationDispatchHoldReason.OriginalUser : ConversationDispatchHoldReason.None);
                cleared.Paused.Should().BeTrue();
                model.CanReleaseConversationDispatch.Should().Be(manualHold);
                if (manualHold)
                {
                    panel.FindControl<Button>("ReleaseDispatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await panel.Operation;
                    var released = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                    released.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.None);
                    released.Paused.Should().BeTrue();
                    model.CanResumePendingConversation.Should().BeTrue();
                }
                foreach (var queued in new[] { b, c })
                {
                    var message = await f.Store.ReadConversationMessageAsync(original, queued.Receipt.Message.Request.RequestId, f.Token);
                    message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                    message.Reason.Should().Be(ModelTurnReason.CallerCancelled);
                    message.Execution.Should().BeNull();
                    message.AssistantResponse.Should().BeNull();
                    message.Body.Should().Be(queued.Receipt.Message.Body);
                    (await f.Store.ReadTaskAsync(original, message.Request.TaskId, f.Token))!.Task.State.Should().Be(HostTaskState.Cancelled);
                }
                active.Completion.IsCompleted.Should().BeFalse();
                otherPending.Completion.IsCompleted.Should().BeFalse();
                (await composed.Conversation.ReadRunningConversationAsync(target, () => true, f.Token)).Should().Be(runningBefore);
                (await composed.Conversation.ReadPendingConversationAsync(otherTarget, () => true, f.Token)).Turns
                    .Should().Contain(item => item.Submission == otherPending.Receipt);
                composed.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                composed.Audit.Events.Count(item => item.ActionId is "runtime.resource.acquire").Should().Be(resources);
                var retentionAfter = await f.Store.ReadRetentionAsync(original, f.Token);
                retentionAfter.Should().Be(retentionBefore, "fixture time is unchanged and no retention domain was rewritten");
                f.Count("conversation_messages").Should().Be(5);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                adapter.ReleaseAll();
                (await active.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); await composed.Conversation.DisposeAsync(); }
        });
    }
}
