using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Controls;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("same-byte-buffer")]
    [InlineData("session-selection")]
    public async Task NativeHeldFreshReceiptRetiresOnlyRowSelectionAndPreservesAcceptedSourceFences(string fault)
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
            var originalTask = await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token);
            var held = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
            await composed.Conversation.ResumePendingConversationAsync(target, held.Revision, () => true, f.Token);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            var inputRevision = 0;
            model.CaptureFreshOrdinaryInput = () =>
            {
                var revision = inputRevision;
                return () => inputRevision == revision;
            };
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single(item => item.Request == source.Request);
                model.ComposerBody = "independent unsent input";
                await model.RefreshPendingConversationAsync();
                await ClickDispatch("HoldDispatch");
                model.CanReleaseConversationDispatch.Should().BeTrue();
                var messages = f.Count("conversation_messages");
                var tasks = f.Count("host_tasks");
                var audits = f.Count("security_audit_events");
                Click("ReviewExpiredOrdinary");
                await window.ConversationOperation;
                model.CanConfirmExpiredOrdinary.Should().BeTrue();
                f.Count("conversation_messages").Should().Be(messages);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                var completed = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
                HostRequest? acceptedRequest = null;
                composed.Conversation.SubmittedConversationCompleted += (_, result) =>
                {
                    if (acceptedRequest is { } request && (result.Receipt?.Message.Request == request
                        || result.Failure is ConversationSubmissionUnconfirmedException uncertain && uncertain.Request == request))
                    { completed.TrySetResult(result); }
                };
                var confirmedAt = f.Time.Now;
                Click("ConfirmExpiredOrdinary");
                await window.ConversationOperation;
                var receiptMessage = model.ConversationMessages.Single();
                acceptedRequest = receiptMessage.Request;
                receiptMessage.OriginalTextSource.Should().Be(source.Request);
                receiptMessage.Outcome.Should().BeNull();
                receiptMessage.Execution.Should().BeNull();
                model.SelectedAnswerRecord.Should().BeNull();
                model.CanConfirmExpiredOrdinary.Should().BeFalse();
                model.ComposerBody.Should().Be("independent unsent input");
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(messages + 1);
                f.Count("host_tasks").Should().Be(tasks + 1);
                await model.RefreshPendingConversationAsync();
                var pending = model.PendingConversationTurns.Single();
                pending.Position.Should().Be(1);
                pending.ExpiresAt.Should().Be(confirmedAt.AddSeconds(15));
                model.CanReleaseConversationDispatch.Should().BeTrue();
                model.CanResumePendingConversation.Should().BeFalse();
                completed.Task.IsCompleted.Should().BeFalse();
                if (fault is "input") { inputRevision++; }
                if (fault is "same-byte-buffer") { model.ComposerBody = model.ComposerBody; }
                if (fault is "session-selection")
                {
                    await model.SelectAsync(null);
                    await model.SelectAsync(model.Sessions.Single());
                }
                if (fault is "cancel")
                {
                    model.SelectedPendingConversation = pending;
                    await model.CancelPendingConversationAsync();
                    model.CanReleaseConversationDispatch.Should().BeTrue();
                }
                await model.RefreshPendingConversationAsync();
                await ClickDispatch("ReleaseDispatch");
                if (fault is "none")
                {
                    var fresh = await adapter.NextAsync();
                    fresh.Provenance.Request.Should().Be(receiptMessage.Request);
                    fresh.Body.Should().Be(source.Body);
                    fresh.Context.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    fresh.Context.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                    fresh.Release();
                }
                var completion = await completed.Task.WaitAsync(f.Token);
                var actual = await f.Store.ReadConversationMessageAsync(target.Authority.SessionId,
                    receiptMessage.Request.RequestId, f.Token);
                if (fault is "none")
                {
                    completion.RequireReceipt().Message.Should().Be(actual);
                    actual.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    actual.AssistantResponse!.Body.Should().Be("answer: " + source.Body);
                    actual.Execution.Should().NotBeNull();
                    adapter.Calls.Should().Be(2);
                }
                else
                {
                    actual.Execution.Should().BeNull();
                    actual.AssistantResponse.Should().BeNull();
                    adapter.Calls.Should().Be(1);
                    if (fault is "cancel")
                    {
                        completion.RequireReceipt().Message.Should().Be(actual);
                        actual.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                        actual.Reason.Should().Be(ModelTurnReason.CallerCancelled);
                    }
                    else
                    {
                        completion.Receipt.Should().BeNull();
                        completion.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>();
                        actual.Should().Be(receiptMessage);
                        (await f.Store.ReadTaskAsync(target.Authority.SessionId, actual.Request.TaskId, f.Token))!
                            .Task.State.Should().Be(HostTaskState.IntentRecorded);
                    }
                }
                var final = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                final.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.None);
                final.Paused.Should().Be(fault is not "none");
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, source.Request.RequestId, f.Token)).Should().Be(source);
                (await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token)).Should().Be(originalTask);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(source.Body).And.NotContain("answer: ");
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); }

            void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task ClickDispatch(string name)
            {
                var panel = window.GetLogicalDescendants().OfType<PendingConversationPanel>().Single();
                panel.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
            }
        });
    }

    [Fact]
    public async Task NativeHistoricalExpiredSourceAfterDoneResumeCannotCreateAReviewOrSubmissionHold()
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
            var originalTask = await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token);
            var done = await composed.Workspace.ChangeLifecycleAsync(target.Authority.SessionId,
                target.Authority.Generation, false, RequestOrigin.LocalUi, f.Token);
            var resumed = await composed.Workspace.ChangeLifecycleAsync(done.SessionId,
                done.Generation, true, RequestOrigin.LocalUi, f.Token);
            resumed.Generation.Value.Should().Be(source.Generation.Value + 2);
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
                model.ComposerBody = "independent current-generation input";
                model.CanReviewExpiredOrdinary.Should().BeFalse();
                var messages = f.Count("conversation_messages");
                var tasks = f.Count("host_tasks");
                var audits = f.Count("security_audit_events");
                await model.ReviewExpiredOrdinaryAsync();
                model.CanConfirmExpiredOrdinary.Should().BeFalse();
                model.ExpiredOrdinaryReviewText.Should().BeEmpty();
                model.ConversationStatus.Should().Contain("exact current session generation");
                await model.ConfirmExpiredOrdinaryAsync();
                model.CanCompose.Should().BeTrue();
                model.ComposerBody.Should().Be("independent current-generation input");
                var commands = composed.Conversation.CreateRequestCommands();
                await commands.Invoking(service => service.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                    resumed.SessionId.Value, resumed.Generation.Value) { RequestExpiredMessage = source },
                    RequestOrigin.LocalUi, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
                f.Count("conversation_messages").Should().Be(messages);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, source.Request.RequestId, f.Token)).Should().Be(source);
                var observedTask = await f.Store.ReadTaskAsync(target.Authority.SessionId, source.Request.TaskId, f.Token);
                observedTask!.Task.Should().Be(originalTask!.Task);
                observedTask.Generation.Should().Be(resumed.Generation);
                observedTask.CurrentSource.Should().BeFalse();
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); }
        });
    }

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
    [InlineData("both-reviews-buffer")]
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
                Func<Task>? oldOutputClick = null;
                if (fault is "both-reviews-buffer")
                {
                    composed.Conversation.BindTaskResponseOutput((mode, _) => new(mode,
                        Kora.Core.Voice.ResponseOutputMode.Hybrid, () => true,
                        (_, _, _, _) => throw new InvalidOperationException("A retired output review cannot present.")));
                    await model.ReviewTaskOutputAsync();
                    oldOutputClick = model.CaptureTaskOutputDecision(confirm: true);
                    model.CanConfirmExpiredOrdinary.Should().BeTrue();
                    model.CanConfirmTaskOutput.Should().BeTrue();
                }
                if (fault is "input" or "call" or "capture" or "owner") { inputEpoch++; }
                if (fault is "same-byte-buffer" or "both-reviews-buffer") { model.ComposerBody = model.ComposerBody; }
                if (fault is "selection") { model.SelectedAnswerRecord = null; }
                if (fault is "cancel-new-review" or "old-click-new-review")
                {
                    model.CancelExpiredOrdinary();
                    await model.ReviewExpiredOrdinaryAsync();
                    if (fault is "cancel-new-review") { await model.CaptureExpiredOrdinaryDecision(confirm: false)(); }
                }
                await oldClick();
                if (oldOutputClick is not null)
                {
                    await oldOutputClick();
                    model.CanConfirmExpiredOrdinary.Should().BeFalse();
                    model.CanConfirmTaskOutput.Should().BeFalse();
                    (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId,
                        source.Request.RequestId, f.Token)).Should().Be(source);
                }
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
