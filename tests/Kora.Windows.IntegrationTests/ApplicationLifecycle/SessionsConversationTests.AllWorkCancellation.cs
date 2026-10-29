using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task StopAllEmptyHostReviewGrantsNoFutureAuthorityAndFreshOrdinaryWorkStillCompletes()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var owner = new Window();
            owner.Show();
            using var questions = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            using var lifecycle = new ApplicationLifecycleController(native.Main, composed.Conversation, native.Queue,
                questions, new(f.Tasks), new(NullLogger<NativeDetailRenderer>.Instance), () => owner);
            var taskCount = f.Count("host_tasks");
            var auditCount = f.Count("security_audit_events");
            try
            {
                var result = await native.Main.StopAllWorkAsync(token: f.Token);
                result.Outcome.Should().Be(AllWorkStopResult.OutcomeKind.NothingToStop);
                result.Reviewed.Should().BeEmpty();
                result.Originals.Should().BeEmpty();
                f.Count("host_tasks").Should().Be(taskCount);
                f.Count("security_audit_events").Should().Be(auditCount);
                var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                var fresh = await composed.Conversation.SubmitQueuedAsync(target, "new ordinary original after the empty observation",
                    () => true, f.Token);
                (await adapter.NextAsync()).Release();
                (await fresh.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
                owner.IsVisible.Should().BeTrue();
                adapter.Calls.Should().Be(1);
            }
            finally { adapter.ReleaseAll(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false, false)]
    [InlineData(RequestOrigin.LocalUi, true, false)]
    public async Task StopAllQuestionRequiresAFreshAnswerThroughItsExactOriginalChannel(
        RequestOrigin origin, bool voiceAnswer, bool accepted)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            f.Time.UseFrozenTimers = true;
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var device = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(device);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            HostQuestionVoiceReplyService? replies = null;
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance,
                questions => replies = new(questions, capture, f.Time, new AllWorkReplyDispatcher(),
                    () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1),
                    (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance));
            await using var replyLifetime = replies!;
            host.BindGate(() => true);
            host.BindAdmissionRevision(() => 1);
            host.BindPrivateRevision(() => 1);
            var parent = new Window();
            parent.Show();
            QuestionWindow? window = null;
            NativeQuestionViewModel? state = null;
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), origin);
            var asking = host.AskApplicationLifecycleAsync(request, ApplicationLifecycleOperation.StopAllWork,
                "Stop all reviewed submitted work, keep Kora open.", f.Time.Now.AddMinutes(1), () => true,
                new(f.Tasks), async model =>
                {
                    state = model;
                    window = new(model, new(NullLogger<NativeDetailRenderer>.Instance));
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Check(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                    {
                        if (window.IsActive && model.IsEditable && !model.HasPendingOperation) { ready.TrySetResult(); }
                    }
                    model.PropertyChanged += Check;
                    try
                    {
                        window.Show(parent);
                        window.Activate();
                        Check(null, new(null));
                        await ready.Task.WaitAsync(f.Token);
                    }
                    finally { model.PropertyChanged -= Check; }
                    shown.TrySetResult();
                }, f.Token);
            try
            {
                await shown.Task.WaitAsync(f.Token);
                var shownWindow = window ?? throw new InvalidOperationException("The exact native question was not shown.");
                shownWindow.FindControl<TextBlock>("TargetLabel")!.Text.Should().Contain("stop-all-work/host-stop-all-work");
                if (voiceAnswer)
                {
                    state!.CanAnswerByVoice.Should().BeTrue(state.VoiceReplyStatus);
                    await state!.AnswerByVoiceAsync();
                    capture.IsListening.Should().BeTrue(state.VoiceReplyStatus);
                    device.QueueTranscript("Confirm: stop reviewed requests, join resources, keep Kora open")();
                }
                else
                {
                    shownWindow.GetLogicalDescendants().OfType<RadioButton>().First().IsChecked = true;
                    shownWindow.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                (await asking).Should().Be(accepted);
                capture.IsListening.Should().BeFalse();
                capture.IsCaptureQuiescent.Should().BeTrue();
                parent.IsVisible.Should().BeTrue();
                replyLifetime.IsActive.Should().BeFalse();
            }
            finally { window?.Close(); parent.Close(); }
        });
    }

    private sealed class AllWorkReplyDispatcher : Kora.Application.IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }

    [Theory]
    [InlineData("typed", "confirm")]
    [InlineData("native", "confirm")]
    [InlineData("typed", "cancel")]
    [InlineData("native", "close")]
    [InlineData("typed", "stale")]
    [InlineData("typed", "dispose")]
    [InlineData("typed", "close-review")]
    [InlineData("native", "close-joining")]
    [InlineData("typed", "notification")]
    public async Task StopAllNativeReviewUsesTheFullAcceptedSetAndKeepsKoraAndUnsentInputOpen(string route, string decision)
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
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var presentation = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, presentation);
            sessions.Show();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            using var questions = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            questions.BindGate(() => native.Main.CanRevealPrivatePresentation);
            questions.BindAdmissionRevision(() => 1);
            questions.BindPrivateRevision(() => composed.Access.ControlRevision);
            using var lifecycle = new ApplicationLifecycleController(native.Main, composed.Conversation, native.Queue,
                questions, new(f.Tasks), new(NullLogger<NativeDetailRenderer>.Instance), () => sessions);
            using var all = new AllWorkViewModel(composed.Workspace, composed.Access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Stop All must not navigate."));
            all.BindStopAll(native.Main.StopAllWorkAsync);
            var overview = new AllWorkWindow(all);
            overview.Show();
            var opened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) => opened.TrySetResult(window));
            var closed = 0;
            native.Main.WindowActionRequested += (_, action) => { if (action == WindowAction.Close) { closed++; } };
            Task? first = null;
            Task? otherSession = null;
            Task? operation = null;
            try
            {
                var otherTarget = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                var seed = await composed.Conversation.SubmitQueuedAsync(otherTarget, "establish an ordinary failure hold",
                    () => true, f.Token);
                var seedInvocation = await adapter.NextAsync();
                seedInvocation.Completion.SetResult(new(ModelTurnOutcome.Denied, seedInvocation.Provenance.Selection, null));
                await seed.Completion;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                model.ComposerBody = "running original outside the foreground session";
                sessions.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                first = sessions.ConversationOperation;
                var running = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                model.ComposerBody = "pending original outside any loaded overview page";
                sessions.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await sessions.ConversationOperation;
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId != original));
                model.ComposerBody.Should().BeEmpty();
                var other = await composed.Conversation.SubmitQueuedAsync(otherTarget, "pending original in the second session",
                    () => true, f.Token);
                otherSession = other.Completion;
                all.Records.Should().BeEmpty("the global operation is not this passive page or its selection");
                if (route is "typed")
                {
                    native.Main.CommandText = "stop all work";
                    operation = native.Main.RunTypedCommand.ExecuteAsync();
                }
                else
                {
                    overview.FindControl<Button>("StopAllSubmittedWork")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    operation = all.StopAllOperation;
                }
                var question = await opened.Task.WaitAsync(f.Token);
                question.FindControl<TextBlock>("TargetLabel")!.Text.Should()
                    .Contain("stop-all-work/host-stop-all-work").And.Contain("LocalUi");
                question.Owner!.GetLogicalDescendants().OfType<TextBlock>().Select(item => item.Text)
                    .Should().Contain(item => item != null && item.Contains("running original outside", StringComparison.Ordinal))
                    .And.Contain(item => item != null && item.Contains("pending original outside", StringComparison.Ordinal));
                first.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(2, "the second session retains its existing failure hold without adding capacity");
                closed.Should().Be(0);
                model.ComposerBody = "unsent text must remain local and unchanged";
                if (decision is "confirm" or "close-joining" or "notification")
                {
                    question.GetLogicalDescendants().OfType<RadioButton>().Single(item =>
                        item.Content is string text && text.StartsWith("Confirm:", StringComparison.Ordinal)).IsChecked = true;
                    var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var cancellingNotifications = 0;
                    var notificationFailures = 0;
                    void Changed(ConversationExecutionStatus status)
                    {
                        if (status.Phase == ConversationExecutionPhase.Cancelling)
                        {
                            Interlocked.Increment(ref cancellingNotifications);
                            if (stopping.TrySetResult() && decision is "notification")
                            {
                                notificationFailures++;
                                throw new IOException("Synthetic stop notification failure.");
                            }
                        }
                    }
                    composed.Conversation.RunningConversationChanged += Changed;
                    try
                    {
                        question.FindControl<Button>("SubmitAnswer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await stopping.Task.WaitAsync(f.Token);
                        operation.IsCompleted.Should().BeFalse("the noncooperative provider has not physically joined");
                        first.IsCompleted.Should().BeFalse();
                        closed.Should().Be(0);
                        if (decision is "close-joining") { overview.Close(); }
                        running.Release();
                        await operation;
                    }
                    finally { composed.Conversation.RunningConversationChanged -= Changed; }
                    cancellingNotifications.Should().BeGreaterThan(1, "the observer remains attached through terminal completion");
                    notificationFailures.Should().Be(decision is "notification" ? 1 : 0);
                    native.Main.LastAllWorkStop!.Outcome.Should().Be(decision is "confirm"
                        ? AllWorkStopResult.OutcomeKind.Completed : AllWorkStopResult.OutcomeKind.PartialUnconfirmed);
                    native.Main.LastAllWorkStop.Originals.Should().HaveCount(3).And.OnlyContain(item =>
                        item.StopAccepted && item.PhysicallyJoined);
                    native.Main.LastAllWorkStop.Originals.Select(item => item.TerminalState)
                        .Should().BeEquivalentTo([HostTaskState.Unknown, HostTaskState.Cancelled, HostTaskState.Cancelled]);
                    (await f.Store.ReadConversationPageAsync(original, null, 25, f.Token)).Messages
                        .Should().HaveCount(2).And.OnlyContain(item => item.Outcome == ModelTurnOutcome.Cancelled && item.AssistantResponse == null);
                }
                else
                {
                    if (decision is "close") { overview.Close(); }
                    else if (decision is "dispose") { lifecycle.Dispose(); }
                    else if (decision is "close-review") { question.Owner.Should().BeOfType<Window>().Subject.Close(); }
                    else
                    {
                        if (decision is "stale") { composed.Access.ControlRevision++; }
                        question.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    await operation;
                    first.IsCompleted.Should().BeFalse();
                    adapter.Calls.Should().Be(2);
                    if (decision is not "dispose")
                    {
                        native.Main.LastAllWorkStop!.Outcome.Should().BeOneOf(
                            AllWorkStopResult.OutcomeKind.Cancelled, AllWorkStopResult.OutcomeKind.Stale);
                    }
                }
                if (decision is "stale")
                { model.ComposerBody.Should().BeEmpty("revoked private ownership must retire the visible composer"); }
                else { model.ComposerBody.Should().Be("unsent text must remain local and unchanged"); }
                sessions.IsVisible.Should().BeTrue();
                closed.Should().Be(0);
            }
            finally
            {
                overview.Close();
                var disposing = composed.Conversation.DisposeAsync();
                adapter.ReleaseAll();
                await disposing;
                if (first is not null) { await first; }
                if (otherSession is not null) { await otherSession; }
                if (operation is not null) { await operation; }
                sessions.ClosePrivate();
                presentation.Revoke();
            }
        });
    }
}
